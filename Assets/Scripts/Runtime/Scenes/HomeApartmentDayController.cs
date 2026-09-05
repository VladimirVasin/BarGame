using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Applies calendar dressing at a safe interaction boundary. Only
    /// dedicated visual groups belong here; collision and interactive props
    /// keep their existing owners throughout a day change.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeApartmentDayController : MonoBehaviour
    {
        private readonly List<DayGroup> groups = new List<DayGroup>();
        private HomeInteriorRoot home;
        private MinigameDebugWindow debugWindow;
        private Action<int> applyDay;

        public bool IsInitialized { get; private set; }
        public int AppliedDayNumber { get; private set; }
        public int PendingDayNumber => HomeApartmentDayRules.ResolveDay(
            GameSessionState.GameDayNumber);

        public void Initialize(HomeInteriorRoot homeRoot, Action<int> apply)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The apartment day controller is already initialized.");
            }

            home = homeRoot != null
                ? homeRoot
                : throw new ArgumentNullException(nameof(homeRoot));
            applyDay = apply;
            IsInitialized = true;
            // Construction must start with the right visual state even
            // when the opening immediately takes the interaction lock.
            Apply(PendingDayNumber);
        }

        public void BindDebugWindow(MinigameDebugWindow window)
        {
            debugWindow = window;
        }

        public void RegisterDayGroup(
            GameObject visualGroup,
            int firstDayNumber,
            int lastDayNumber)
        {
            if (visualGroup == null)
            {
                throw new ArgumentNullException(nameof(visualGroup));
            }

            if (firstDayNumber < HomeApartmentDayRules.FirstDayNumber ||
                lastDayNumber > HomeApartmentDayRules.LastDayNumber ||
                firstDayNumber > lastDayNumber)
            {
                throw new ArgumentOutOfRangeException(nameof(firstDayNumber),
                    "A visual group requires an inclusive day range within 1-7.");
            }

            for (int index = 0; index < groups.Count; index++)
            {
                if (groups[index].Root == visualGroup)
                {
                    throw new InvalidOperationException(
                        "An apartment visual group can be registered only once.");
                }
            }

            var group = new DayGroup(
                visualGroup, firstDayNumber, lastDayNumber);
            groups.Add(group);
            if (IsInitialized)
            {
                group.Apply(AppliedDayNumber);
            }
        }

        public bool RefreshImmediate()
        {
            if (!IsInitialized || AppliedDayNumber == PendingDayNumber ||
                IsHomeBusy(home) ||
                (BarMinigameModalLock.IsAnyLocked &&
                 (debugWindow == null || !debugWindow.IsOpen)))
            {
                return false;
            }

            Apply(PendingDayNumber);
            return true;
        }

        public static bool IsHomeBusy(HomeInteriorRoot homeRoot)
        {
            if (homeRoot == null)
            {
                return false;
            }

            if (SceneTransitionService.IsTransitioning ||
                (homeRoot.Opening != null &&
                 homeRoot.Opening.isActiveAndEnabled &&
                 homeRoot.Opening.Phase != HomeOpeningPhase.Complete) ||
                (homeRoot.AnimatedInteraction != null &&
                 homeRoot.AnimatedInteraction.IsActive))
            {
                return true;
            }

            PlayerDoorActionController doorAction =
                homeRoot.Player.GameObject == null
                    ? null
                    : homeRoot.Player.GameObject.GetComponent<
                        PlayerDoorActionController>();
            return doorAction != null && doorAction.IsPlaying;
        }

        private void Update()
        {
            RefreshImmediate();
        }

        private void Apply(int dayNumber)
        {
            for (int index = 0; index < groups.Count; index++)
            {
                groups[index].Apply(dayNumber);
            }

            applyDay?.Invoke(dayNumber);
            AppliedDayNumber = dayNumber;
        }

        private readonly struct DayGroup
        {
            public DayGroup(GameObject root, int firstDay, int lastDay)
            {
                Root = root;
                FirstDay = firstDay;
                LastDay = lastDay;
            }

            public GameObject Root { get; }
            private int FirstDay { get; }
            private int LastDay { get; }

            public void Apply(int dayNumber)
            {
                if (Root != null)
                {
                    Root.SetActive(dayNumber >= FirstDay && dayNumber <= LastDay);
                }
            }
        }
    }
}
