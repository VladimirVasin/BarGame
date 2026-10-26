using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One bounded loan of the present audience's ordinary attention.</summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class BarKaraokeAudience : MonoBehaviour
    {
        private readonly List<BarPatron> patrons = new List<BarPatron>(BarBusinessHours.MaximumGuests);
        private BarInteriorRoot bar;
        private BarCrowdController crowd;
        private Transform singerHead;
        private NpcHeroAttentionLook bartenderLook;
        private bool initialized, subscribed, requested, applied;

        public bool IsPerforming => applied;
        public int AttendingPatronCount
        {
            get
            {
                if (!applied) return 0;
                int count = 0;
                foreach (BarPatron patron in patrons)
                    if (patron.Drinking != null && patron.Drinking.IsKaraokeAttending(this)) count++;
                return count;
            }
        }
        public bool IsBartenderAttending => applied && bartenderLook != null &&
            bartenderLook.IsFocusOverrideAttending(this);

        public void Initialize(BarInteriorRoot owner)
        {
            if (initialized) throw new InvalidOperationException("The karaoke audience is already initialized.");
            if (owner == null || owner.Crowd == null || owner.Player.GameObject == null)
                throw new ArgumentException("Build the bar cast and player before its karaoke audience.", nameof(owner));
            bar = owner;
            crowd = owner.Crowd;
            var presentation = owner.Player.Visual as Player3DCharacterPresentation;
            singerHead = presentation != null ? presentation.Registry.Anchors.Head : null;
            if (singerHead == null) throw new InvalidOperationException("The audience needs the actual singing hero's head.");
            bartenderLook = owner.Bartender != null ? owner.Bartender.GetComponent<NpcHeroAttentionLook>() : null;
            initialized = true;
            Connect();
            RefreshPatrons();
        }

        public void SetPerforming(bool performing)
        {
            requested = performing;
            Synchronize();
        }

        public void Clear()
        {
            requested = false;
            Release();
        }

        private void Update() => Synchronize();

        private void Synchronize()
        {
            if (!initialized) return;
            bool available = requested && isActiveAndEnabled && bar != null && bar.isActiveAndEnabled &&
                bar.IsBusinessOpen && singerHead != null && singerHead.gameObject.activeInHierarchy &&
                GameCursor.ApplicationFocused && !GameTimeScaleRuntime.IsPaused &&
                !PauseMenuController.IsAnyPaused && !SceneTransitionService.IsTransitioning;
            if (!available)
            {
                if (applied) Release();
                return;
            }
            applied = true;
            foreach (BarPatron patron in patrons)
            {
                if (patron.Drinking == null) continue;
                if (!patron.IsTemporarilyAway && patron.Drinking.isActiveAndEnabled)
                    patron.Drinking.TrySetKaraokeFocus(this, singerHead);
                else patron.Drinking.ClearKaraokeFocus(this);
            }
            if (bartenderLook != null && bartenderLook.isActiveAndEnabled)
                bartenderLook.TrySetFocusOverride(this, singerHead);
        }

        private void RefreshPatrons()
        {
            if (!initialized || crowd == null) return;
            IReadOnlyList<BarPatron> present = crowd.Patrons;
            for (int index = patrons.Count - 1; index >= 0; index--)
            {
                BarPatron previous = patrons[index];
                bool retained = false;
                for (int current = 0; current < present.Count; current++)
                    if (ReferenceEquals(previous, present[current]) && !previous.IsTemporarilyAway)
                    { retained = true; break; }
                if (retained) continue;
                if (previous.Drinking != null) previous.Drinking.ClearKaraokeFocus(this);
                patrons.RemoveAt(index);
            }
            for (int index = 0; index < present.Count; index++)
            {
                BarPatron patron = present[index];
                if (patron == null || patron.IsTemporarilyAway || patron.Drinking == null || patrons.Contains(patron)) continue;
                if (patrons.Count >= BarBusinessHours.MaximumGuests)
                    throw new InvalidOperationException("The karaoke audience exceeded the bar's finite cast.");
                patrons.Add(patron);
            }
            Synchronize();
        }

        private void Release()
        {
            applied = false;
            foreach (BarPatron patron in patrons)
                if (patron.Drinking != null) patron.Drinking.ClearKaraokeFocus(this);
            if (bartenderLook != null) bartenderLook.ClearFocusOverride(this);
        }

        private void Connect()
        {
            if (!initialized || subscribed || crowd == null) return;
            crowd.PresenceChanged += RefreshPatrons;
            subscribed = true;
        }

        private void Disconnect()
        {
            if (subscribed && crowd != null) crowd.PresenceChanged -= RefreshPatrons;
            subscribed = false;
        }

        private void OnApplicationFocus(bool focused) { if (!focused) Release(); }
        private void OnEnable() { Connect(); if (initialized) RefreshPatrons(); }
        private void OnDisable() { Clear(); Disconnect(); }
        private void OnDestroy() { Clear(); Disconnect(); patrons.Clear(); }
    }
}
