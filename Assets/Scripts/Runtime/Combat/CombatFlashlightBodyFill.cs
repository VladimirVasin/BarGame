using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>A short, hero-only approximation of spill from the one physical chest lamp.</summary>
    internal sealed class CombatFlashlightBodyFill : IDisposable
    {
        internal const uint HeroLayer = 1u << 6;
        internal const float BaseIntensity = .09f, MaximumIntensity = .24f, Range = 1.25f;
        private const float ReceiverScanInterval = .1f, RiseSeconds = .18f, FallSeconds = .32f;
        private readonly Player3DCharacterPresentation presentation;
        private readonly Transform lens;
        private readonly Light spot;
        private readonly Renderer lensRenderer;
        private readonly Dictionary<Renderer, ReceiverState> receivers = new Dictionary<Renderer, ReceiverState>(256);
        private readonly List<Renderer> removals = new List<Renderer>(256);
        private float scanRemaining;
        private uint generation;
        private bool active, disposed;

        private struct ReceiverState
        {
            internal bool HadLayer;
            internal uint Generation;
        }

        internal Light Source { get; private set; }
        internal float BounceAmount { get; private set; }

        internal CombatFlashlightBodyFill(Player3DCharacterPresentation presentation, Transform lens, Light spot, Renderer lensRenderer)
        {
            if (presentation == null || lens == null || spot == null || lensRenderer == null)
                throw new ArgumentNullException();
            this.presentation = presentation;
            this.lens = lens;
            this.spot = spot;
            this.lensRenderer = lensRenderer;
            var host = new GameObject("Chest Lamp Body Spill");
            host.transform.SetParent(lens, false);
            Source = host.AddComponent<Light>();
            Source.enabled = false;
            Source.type = LightType.Point;
            Source.range = Range;
            Source.color = spot.color;
            Source.intensity = 0f;
            Source.shadows = LightShadows.None;
            Source.bounceIntensity = 0f;
            Source.renderMode = LightRenderMode.ForcePixel;
            Source.GetUniversalAdditionalLightData().renderingLayers = HeroLayer;
        }

        internal void SetActive(bool value)
        {
            if (disposed) return;
            bool changed = active != value;
            active = value;
            Synchronize(0f, changed);
        }

        internal void Advance(float seconds, float targetBounce, bool paused)
        {
            if (!float.IsFinite(seconds) || seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!float.IsFinite(targetBounce)) throw new ArgumentOutOfRangeException(nameof(targetBounce));
            if (disposed) return;
            if (active && !paused && seconds > 0f)
            {
                float target = Mathf.Clamp01(targetBounce);
                float response = target > BounceAmount ? RiseSeconds : FallSeconds;
                float blend = 1f - Mathf.Exp(-seconds / response);
                BounceAmount = Mathf.Lerp(BounceAmount, target, blend);
                if (Mathf.Abs(BounceAmount - target) < .0001f) BounceAmount = target;
            }
            // Visibility and rig attachment still synchronize during a pause;
            // only the bounce amount and presentation clock are held.
            Synchronize(paused ? 0f : seconds, false);
        }

        internal void Reset()
        {
            if (disposed) return;
            BounceAmount = 0f;
            scanRemaining = 0f;
            Synchronize(0f, true);
        }

        private void Synchronize(float seconds, bool forceScan)
        {
            if (Source == null) return;
            bool visible = active && presentation != null && lens != null && spot != null && spot.isActiveAndEnabled &&
                spot.intensity > 0f && lensRenderer != null && lensRenderer.enabled && !lensRenderer.forceRenderingOff &&
                lensRenderer.gameObject.activeInHierarchy && lensRenderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly;
            if (!visible)
            {
                Source.enabled = false;
                Source.intensity = 0f;
                RestoreReceivers();
                scanRemaining = 0f;
                return;
            }
            bool enabling = !Source.enabled;
            Source.transform.position = lens.position + lens.forward * .24f + Vector3.up * .07f;
            Source.color = spot.color;
            Source.intensity = Mathf.Lerp(BaseIntensity, MaximumIntensity, BounceAmount);
            scanRemaining -= seconds;
            if (enabling || forceScan || scanRemaining <= 0f)
            {
                RefreshReceivers();
                scanRemaining = ReceiverScanInterval;
            }
            Source.enabled = true;
        }

        private void RefreshReceivers()
        {
            generation = unchecked(generation + 1u);
            IReadOnlyList<Renderer> current = presentation.Renderers;
            for (int index = 0; index < current.Count; index++)
            {
                Renderer renderer = current[index];
                if (renderer == null || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;
                uint mask = renderer.renderingLayerMask;
                if (!receivers.TryGetValue(renderer, out ReceiverState state))
                    state.HadLayer = (mask & HeroLayer) != 0;
                state.Generation = generation;
                receivers[renderer] = state;
                if ((mask & HeroLayer) == 0) renderer.renderingLayerMask = mask | HeroLayer;
            }
            removals.Clear();
            foreach (KeyValuePair<Renderer, ReceiverState> entry in receivers)
            {
                if (entry.Key != null && entry.Value.Generation == generation) continue;
                RestoreReceiver(entry.Key, entry.Value.HadLayer);
                removals.Add(entry.Key);
            }
            for (int index = 0; index < removals.Count; index++) receivers.Remove(removals[index]);
            removals.Clear();
        }

        private static void RestoreReceiver(Renderer renderer, bool hadLayer)
        {
            // Other light/inspection owners can change their bits during this
            // lease. Never restore an old whole mask over those newer changes.
            if (renderer != null && !hadLayer)
                renderer.renderingLayerMask &= ~HeroLayer;
        }

        private void RestoreReceivers()
        {
            foreach (KeyValuePair<Renderer, ReceiverState> entry in receivers)
                RestoreReceiver(entry.Key, entry.Value.HadLayer);
            receivers.Clear();
            removals.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            active = false;
            disposed = true;
            RestoreReceivers();
            if (Source == null) return;
            Source.enabled = false;
            GameObject host = Source.gameObject;
            Source = null;
            if (Application.isPlaying) UnityEngine.Object.Destroy(host);
            else UnityEngine.Object.DestroyImmediate(host);
        }
    }
}
