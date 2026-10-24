using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One actual vessel left at a visit slot. It deliberately retains no
    /// guest component: the guest may be destroyed before collection starts.
    /// The crowd owns its spatial reservation; the collector owns its motion.
    /// </summary>
    public sealed class BarAbandonedDrink
    {
        internal BarAbandonedDrink(int slot, BarNpcAnchor anchor,
            BarPatronDrinkProp drinkProp, Renderer surfaceRenderer)
        {
            Slot = slot;
            Anchor = anchor;
            DrinkProp = drinkProp ?? throw new ArgumentNullException(nameof(drinkProp));
            SurfaceRenderer = surfaceRenderer != null ? surfaceRenderer :
                throw new ArgumentNullException(nameof(surfaceRenderer));
        }

        internal int Slot { get; }
        public BarNpcAnchor Anchor { get; }
        public string AnchorId => Anchor.Id;
        public BarPatronDrinkProp DrinkProp { get; }
        public Renderer SurfaceRenderer { get; }
        public bool IsReadyForCollection { get; internal set; }
        public bool IsCleanupComplete { get; internal set; }
    }
}
