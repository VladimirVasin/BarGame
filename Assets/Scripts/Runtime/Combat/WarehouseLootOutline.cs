using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bounded shelf affordance over the shared depth-tested control contour.</summary>
    internal sealed class WarehouseLootOutline : IDisposable
    {
        internal const float VisibilityRadius = 6f;
        private readonly Transform model;
        private readonly WorldControlOutlineView view;
        internal int RenderedPartCount => view.RenderedPartCount;

        internal WarehouseLootOutline(Transform model)
        {
            this.model = model;
            view = new WorldControlOutlineView(lootHighlight: true);
            VodkaBottleView bottle = model.GetComponent<VodkaBottleView>();
            if (bottle != null)
            {
                // Glass cells and cap define the outside. Printed letters,
                // paper, fill discs and liquid must not acquire inner contours.
                var parts = new Renderer[bottle.GlassCells.Count + 1];
                for (int index = 0; index < bottle.GlassCells.Count; index++) parts[index] = bottle.GlassCells[index];
                parts[parts.Length - 1] = bottle.Cap.GetComponent<Renderer>();
                view.Select(parts);
            }
            else view.Select(model.GetComponentsInChildren<Renderer>(true));
        }

        internal void Present(Camera camera, Transform hero, bool available)
        {
            if (!available || model == null || hero == null || camera == null ||
                !camera.isActiveAndEnabled || !model.gameObject.activeInHierarchy ||
                (model.position - hero.position).sqrMagnitude > VisibilityRadius * VisibilityRadius ||
                GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused ||
                CounterMenuInput.IsBlockedByOtherUi() || SceneTransitionService.IsTransitioning ||
                camera.TryGetComponent(out Rendering.ScenePresentationExclusion _))
            { view.Hide(); return; }
            // The common shader tests real scene depth. Closed leaves are also
            // gated by the pickup's existing source-availability delegate.
            view.Present(camera);
        }

        internal void Hide() => view.Hide();
        public void Dispose() => view.Dispose();
    }
}
