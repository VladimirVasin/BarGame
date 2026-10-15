using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>Owner-scoped cursor modes: gameplay, modal UI and contextual views.</summary>
    public static class GameCursor
    {
        private struct Lease
        {
            public object Owner;
            public CursorLockMode Mode;
            public bool Visible;
            public int Priority;
        }

        private const int MaximumLeases = 32;
        private static readonly Lease[] leases = new Lease[MaximumLeases];
        private static int count;
        private static bool focused = true, baselineSaved, baselineVisible;
        private static CursorLockMode baselineMode;
        private static bool freeCursorRequested, gameplayMouseInputAllowed;

        public static bool ApplicationFocused => focused;

        public static bool FreeCursorRequested
        {
            get { Refresh(); return freeCursorRequested; }
        }

        public static bool GameplayMouseInputAllowed
        {
            get { Refresh(); return gameplayMouseInputAllowed; }
        }

        /// <summary>Changes whenever mouse input changes ownership, including a full handoff between samples.</summary>
        public static int MouseLookRevision { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            if (baselineSaved) Apply(baselineMode, baselineVisible);
            Array.Clear(leases, 0, count);
            count = 0;
            baselineSaved = false;
            focused = true;
            freeCursorRequested = gameplayMouseInputAllowed = false;
            MouseLookRevision++;
        }

        /// <param name="priority">Gameplay uses 0, fullscreen modals 5, contextual views 10.</param>
        public static void Capture(object owner, CursorLockMode mode, bool visible, int priority = 10)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            PruneDestroyedOwners();
            int index = 0;
            while (index < count && !ReferenceEquals(leases[index].Owner, owner)) index++;
            if (index == count)
            {
                if (count == MaximumLeases) throw new InvalidOperationException("Too many active cursor owners.");
                if (!baselineSaved)
                {
                    baselineMode = Cursor.lockState;
                    baselineVisible = Cursor.visible;
                    baselineSaved = true;
                }
                count++;
            }
            leases[index] = new Lease { Owner = owner, Mode = mode, Visible = visible, Priority = priority };
            Refresh();
        }

        public static void Release(object owner)
        {
            for (int i = count - 1; i >= 0; i--)
                if (ReferenceEquals(leases[i].Owner, owner)) RemoveAt(i);
            Refresh();
        }

        public static void SetApplicationFocused(bool value)
        {
            focused = value;
            Refresh();
        }

        public static void Refresh()
        {
            PruneDestroyedOwners();
            int best = -1;
            for (int i = 0; i < count; i++)
                if (best < 0 || leases[i].Priority >= leases[best].Priority) best = i;

            Keyboard keyboard = Keyboard.current;
            bool alt = keyboard != null && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);
            freeCursorRequested = focused && best >= 0 && leases[best].Priority == 0 && alt;
            bool allow = focused && best >= 0 && leases[best].Priority == 0 && !alt;
            if (allow != gameplayMouseInputAllowed)
            {
                gameplayMouseInputAllowed = allow;
                MouseLookRevision++;
            }

            if (best >= 0)
            {
                if (!focused || freeCursorRequested) Apply(CursorLockMode.None, true);
                else Apply(leases[best].Mode, leases[best].Visible);
            }
            else if (baselineSaved)
            {
                Apply(focused ? baselineMode : CursorLockMode.None, !focused || baselineVisible);
                if (focused) baselineSaved = false;
            }
        }

        private static void Apply(CursorLockMode mode, bool visible)
        {
            if (Cursor.lockState != mode) Cursor.lockState = mode;
            if (Cursor.visible != visible) Cursor.visible = visible;
        }

        private static void PruneDestroyedOwners()
        {
            for (int i = count - 1; i >= 0; i--)
                if (leases[i].Owner is UnityEngine.Object owner && owner == null) RemoveAt(i);
        }

        private static void RemoveAt(int index)
        {
            count--;
            for (int i = index; i < count; i++) leases[i] = leases[i + 1];
            leases[count] = default;
        }
    }
}
