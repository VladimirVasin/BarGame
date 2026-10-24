using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Only the trading floor dims at closing; upstairs and the landmark retain their owners.</summary>
    [DisallowMultipleComponent]
    public sealed class BarFacadeHoursPresentation : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private Renderer[] panes;
        private MaterialPropertyBlock[] original;
        private MaterialPropertyBlock properties;
        private int minute = -1;
        private float nightFactor = -1f;

        public bool IsBusinessOpen { get; private set; }

        public void Initialize(IReadOnlyList<Renderer> groundWindows)
        {
            if (panes != null) throw new InvalidOperationException("Bar window hours are already initialized.");
            if (groundWindows == null) throw new ArgumentNullException(nameof(groundWindows));
            properties = new MaterialPropertyBlock();
            panes = new Renderer[groundWindows.Count];
            original = new MaterialPropertyBlock[panes.Length];
            for (int index = 0; index < panes.Length; index++)
            {
                panes[index] = groundWindows[index];
                original[index] = new MaterialPropertyBlock();
                if (panes[index] != null) panes[index].GetPropertyBlock(original[index]);
            }
            Refresh();
        }

        private void LateUpdate()
        {
            if (minute != GameSessionState.GameMinuteOfDay ||
                nightFactor != CityWindowAppearance.NightFactor) Refresh();
        }

        private void OnEnable() { minute = -1; }

        public void Refresh()
        {
            if (panes == null) return;
            minute = GameSessionState.GameMinuteOfDay;
            nightFactor = CityWindowAppearance.NightFactor;
            IsBusinessOpen = BarBusinessHours.IsOpen(minute);
            for (int index = 0; index < panes.Length; index++)
            {
                Renderer pane = panes[index];
                if (pane == null || pane.sharedMaterial == null) continue;
                if (IsBusinessOpen)
                {
                    pane.SetPropertyBlock(original[index]);
                    continue;
                }
                Material material = pane.sharedMaterial;
                pane.GetPropertyBlock(properties);
                Color color = Color.Lerp(CityWindowAppearance.DayGlass, material.GetColor(BaseColorId), .12f);
                properties.SetColor(BaseColorId, color);
                properties.SetColor(ColorId, color);
                properties.SetColor(EmissionColorId, material.GetColor(EmissionColorId) * .08f);
                pane.SetPropertyBlock(properties);
            }
        }

        private void OnDisable()
        {
            if (panes == null) return;
            for (int index = 0; index < panes.Length; index++)
                if (panes[index] != null) panes[index].SetPropertyBlock(original[index]);
        }
    }
}
