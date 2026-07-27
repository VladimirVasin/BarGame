using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class CityMapController : MonoBehaviour
    {
        private enum CommandType
        {
            ToggleMap,
            ToggleBar,
            MoveBar,
            ClearRoute
        }

        private readonly struct PendingCommand
        {
            public PendingCommand(
                CommandType type,
                int barIndex = -1,
                string barId = "",
                int direction = 0)
            {
                Type = type;
                BarIndex = barIndex;
                BarId = barId ?? string.Empty;
                Direction = direction;
            }

            public CommandType Type { get; }
            public int BarIndex { get; }
            public string BarId { get; }
            public int Direction { get; }
        }

        private readonly List<BuildingLot> bars = new List<BuildingLot>();
        private readonly List<BuildingLot> orderedStops =
            new List<BuildingLot>();
        private readonly Queue<PendingCommand> pendingCommands =
            new Queue<PendingCommand>();

        private PlayerRuntime player;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView intoxicationHud;
        private bool previousMotorInput;
        private bool previousInteractorInput;
        private bool previousOrbitInput;
        private bool previousHudVisibility;
        private int inputUnlockFrame;

        public bool IsInitialized { get; private set; }
        public bool IsOpen { get; private set; }
        public CityLayout Layout { get; private set; }
        public IReadOnlyList<BuildingLot> Bars => bars;
        public IReadOnlyList<string> Route => GameSessionState.PlannedBarRoute;
        public CityRoutePath CurrentPath { get; private set; }
        public int SelectedBarIndex { get; private set; }
        public CityMapView View { get; private set; }
        public Vector3 PlayerWorldPosition =>
            player.GameObject == null
                ? Vector3.zero
                : player.GameObject.transform.position;
        public Vector3 PlayerForward =>
            player.GameObject == null
                ? Vector3.forward
                : player.GameObject.transform.forward;

        public void Initialize(
            CityLayout layout,
            PlayerRuntime playerRuntime,
            PlayerCameraFollow follow,
            IntoxicationHudView hud)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            player = playerRuntime;
            cameraFollow = follow;
            intoxicationHud = hud;

            bars.Clear();
            for (int index = 0; index < Layout.BuildingLots.Count; index++)
            {
                BuildingLot lot = Layout.BuildingLots[index];
                if (lot.IsBar)
                {
                    bars.Add(lot);
                }
            }

            bars.Sort(CompareBarLots);
            SelectedBarIndex = bars.Count == 0
                ? -1
                : Mathf.Clamp(SelectedBarIndex, 0, bars.Count - 1);

            View = GetComponent<CityMapView>();
            if (View == null)
            {
                View = gameObject.AddComponent<CityMapView>();
            }

            View.Initialize(this);
            IsInitialized = true;
            RefreshPath();
        }

        public bool Open()
        {
            if (!IsInitialized ||
                IsOpen ||
                SceneTransitionService.IsTransitioning)
            {
                return false;
            }

            previousMotorInput =
                player.Motor != null && player.Motor.InputEnabled;
            previousInteractorInput =
                player.Interactor != null && player.Interactor.InputEnabled;
            previousOrbitInput =
                cameraFollow != null && cameraFollow.OrbitInputEnabled;
            previousHudVisibility =
                intoxicationHud == null || intoxicationHud.Visible;

            player.Motor?.SetInputEnabled(false);
            player.Interactor?.SetInputEnabled(false);
            cameraFollow?.SetOrbitInputEnabled(false);
            if (intoxicationHud != null)
            {
                intoxicationHud.Visible = false;
            }

            inputUnlockFrame = Time.frameCount + 1;
            IsOpen = true;
            RefreshPath();
            RetroAudio.Play(RetroSfxId.MapOpen);
            return true;
        }

        public bool Close()
        {
            return Close(true);
        }

        private bool Close(bool playSound)
        {
            if (!IsOpen)
            {
                return false;
            }

            IsOpen = false;
            player.Motor?.SetInputEnabled(previousMotorInput);
            player.Interactor?.SetInputEnabled(previousInteractorInput);
            cameraFollow?.SetOrbitInputEnabled(previousOrbitInput);
            if (intoxicationHud != null)
            {
                intoxicationHud.Visible = previousHudVisibility;
            }

            if (playSound)
            {
                RetroAudio.Play(RetroSfxId.UiCancel);
            }

            return true;
        }

        public bool ToggleBar(int barIndex)
        {
            if (!IsValidBarIndex(barIndex))
            {
                return false;
            }

            SelectedBarIndex = barIndex;
            string barId = bars[barIndex].BarId;
            bool wasSelected = GetRouteOrder(barId) >= 0;
            if (wasSelected)
            {
                GameSessionState.RemoveRouteStop(barId);
            }
            else
            {
                GameSessionState.TryAddRouteStop(barId);
            }

            RefreshPath();
            bool changed =
                wasSelected != (GetRouteOrder(barId) >= 0);
            if (changed)
            {
                RetroAudio.Play(RetroSfxId.UiConfirm);
            }

            return changed;
        }

        public bool MoveBar(string barId, int direction)
        {
            int previousIndex = GetRouteOrder(barId);
            if (previousIndex < 0 || direction == 0)
            {
                return false;
            }

            int barIndex = FindBarIndex(barId);
            if (barIndex >= 0)
            {
                SelectedBarIndex = barIndex;
            }

            GameSessionState.MoveRouteStop(barId, direction);
            RefreshPath();
            bool changed = GetRouteOrder(barId) != previousIndex;
            if (changed)
            {
                RetroAudio.Play(RetroSfxId.UiMove);
            }

            return changed;
        }

        public bool ClearRoute()
        {
            if (GameSessionState.PlannedBarRoute.Count == 0)
            {
                return false;
            }

            GameSessionState.ClearRoute();
            RefreshPath();
            RetroAudio.Play(RetroSfxId.UiCancel);
            return true;
        }

        public int GetRouteOrder(string barId)
        {
            IReadOnlyList<string> route = GameSessionState.PlannedBarRoute;
            for (int index = 0; index < route.Count; index++)
            {
                if (string.Equals(
                    route[index],
                    barId,
                    StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        public int FindBarIndex(string barId)
        {
            for (int index = 0; index < bars.Count; index++)
            {
                if (string.Equals(
                    bars[index].BarId,
                    barId,
                    StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        public string GetBarLabel(int barIndex)
        {
            return IsValidBarIndex(barIndex)
                ? string.Format(
                    LocalizationService.Get("map.bar_name"),
                    barIndex + 1)
                : string.Empty;
        }

        public void QueueToggleMap()
        {
            pendingCommands.Enqueue(
                new PendingCommand(CommandType.ToggleMap));
        }

        public void QueueToggleBar(int barIndex)
        {
            pendingCommands.Enqueue(
                new PendingCommand(
                    CommandType.ToggleBar,
                    barIndex: barIndex));
        }

        public void QueueMoveBar(string barId, int direction)
        {
            pendingCommands.Enqueue(
                new PendingCommand(
                    CommandType.MoveBar,
                    barId: barId,
                    direction: direction));
        }

        public void QueueClearRoute()
        {
            pendingCommands.Enqueue(
                new PendingCommand(CommandType.ClearRoute));
        }

        private void Update()
        {
            ProcessQueuedCommands();

            if (!IsInitialized)
            {
                return;
            }

            if (!IsOpen)
            {
                if (WasMapTogglePressed())
                {
                    Open();
                }

                return;
            }

            if (SceneTransitionService.IsTransitioning)
            {
                Close(false);
                return;
            }

            if (Time.frameCount <= inputUnlockFrame)
            {
                return;
            }

            if (WasMapTogglePressed() || WasCancelPressed())
            {
                Close();
                return;
            }

            if (WasClearPressed())
            {
                ClearRoute();
                return;
            }

            int selectionDelta = ReadSelectionDelta();
            if (selectionDelta != 0)
            {
                MoveSelection(selectionDelta);
            }

            int routeMove = ReadRouteMove();
            if (routeMove != 0 && IsValidBarIndex(SelectedBarIndex))
            {
                MoveBar(bars[SelectedBarIndex].BarId, routeMove);
            }

            if (WasConfirmPressed() && IsValidBarIndex(SelectedBarIndex))
            {
                ToggleBar(SelectedBarIndex);
            }
        }

        private void OnDisable()
        {
            pendingCommands.Clear();
            Close(false);
        }

        private void OnDestroy()
        {
            Close(false);
        }

        private void ProcessQueuedCommands()
        {
            while (pendingCommands.Count > 0)
            {
                PendingCommand command = pendingCommands.Dequeue();
                switch (command.Type)
                {
                    case CommandType.ToggleMap:
                        if (IsOpen)
                        {
                            Close();
                        }
                        else
                        {
                            Open();
                        }

                        break;
                    case CommandType.ToggleBar:
                        if (IsOpen)
                        {
                            ToggleBar(command.BarIndex);
                        }

                        break;
                    case CommandType.MoveBar:
                        if (IsOpen)
                        {
                            MoveBar(command.BarId, command.Direction);
                        }

                        break;
                    case CommandType.ClearRoute:
                        if (IsOpen)
                        {
                            ClearRoute();
                        }

                        break;
                }
            }
        }

        private void RefreshPath()
        {
            if (!IsInitialized && Layout == null)
            {
                return;
            }

            RemoveUnknownRouteStops();
            orderedStops.Clear();
            IReadOnlyList<string> route = GameSessionState.PlannedBarRoute;
            for (int index = 0; index < route.Count; index++)
            {
                int barIndex = FindBarIndex(route[index]);
                if (barIndex >= 0)
                {
                    orderedStops.Add(bars[barIndex]);
                }
            }

            CurrentPath = CityRoutePathfinder.Build(
                Layout,
                PlayerWorldPosition,
                orderedStops);
        }

        private void RemoveUnknownRouteStops()
        {
            IReadOnlyList<string> route =
                GameSessionState.PlannedBarRoute;
            for (int index = route.Count - 1; index >= 0; index--)
            {
                if (FindBarIndex(route[index]) < 0)
                {
                    GameSessionState.RemoveRouteStop(route[index]);
                }
            }
        }

        private void MoveSelection(int delta)
        {
            if (bars.Count == 0 || delta == 0)
            {
                SelectedBarIndex = -1;
                return;
            }

            int previousIndex = SelectedBarIndex;
            SelectedBarIndex = (SelectedBarIndex + Math.Sign(delta)) % bars.Count;
            if (SelectedBarIndex < 0)
            {
                SelectedBarIndex += bars.Count;
            }

            if (SelectedBarIndex != previousIndex)
            {
                RetroAudio.Play(RetroSfxId.UiMove);
            }
        }

        private bool IsValidBarIndex(int index)
        {
            return index >= 0 && index < bars.Count;
        }

        private static int CompareBarLots(BuildingLot left, BuildingLot right)
        {
            int rowComparison = left.Cell.y.CompareTo(right.Cell.y);
            return rowComparison != 0
                ? rowComparison
                : left.Cell.x.CompareTo(right.Cell.x);
        }

        private static bool WasMapTogglePressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.mKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.selectButton.wasPressedThisFrame;
        }

        private static bool WasCancelPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonEast.wasPressedThisFrame;
        }

        private static bool WasConfirmPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.eKey.wasPressedThisFrame ||
                 keyboard.enterKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonSouth.wasPressedThisFrame;
        }

        private static bool WasClearPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonWest.wasPressedThisFrame;
        }

        private static int ReadSelectionDelta()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.wasPressedThisFrame)
                {
                    return -1;
                }

                if (keyboard.rightArrowKey.wasPressedThisFrame)
                {
                    return 1;
                }
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.dpad.left.wasPressedThisFrame)
                {
                    return -1;
                }

                if (gamepad.dpad.right.wasPressedThisFrame)
                {
                    return 1;
                }
            }

            return 0;
        }

        private static int ReadRouteMove()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.upArrowKey.wasPressedThisFrame)
                {
                    return -1;
                }

                if (keyboard.downArrowKey.wasPressedThisFrame)
                {
                    return 1;
                }
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.leftShoulder.wasPressedThisFrame)
                {
                    return -1;
                }

                if (gamepad.rightShoulder.wasPressedThisFrame)
                {
                    return 1;
                }
            }

            return 0;
        }
    }
}
