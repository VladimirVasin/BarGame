using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Keeps the register of holes cut out of the cemetery's ground
    /// slab and rebuilds the slab whenever a new one is opened. This
    /// is the whole trick behind a dug grave: the ground is not
    /// painted over or pushed down, its rectangle is simply gone, and
    /// the slab's own thickness becomes the walls of the hole.
    ///
    /// Lives on the surfaces root beside the slab it owns, so the
    /// rebuilt object lands back in the same place in the hierarchy.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityCemeteryGroundExcavation : MonoBehaviour
    {
        private readonly List<Rect> cuts = new List<Rect>();
        private CityLayout layout;
        private GameObject ground;

        /// <summary>Every hole open in the slab right now.</summary>
        public IReadOnlyList<Rect> Cuts => cuts;

        /// <summary>The slab object as it stands. Replaced outright on
        /// every excavation, so never cache it across a dig.</summary>
        public GameObject Ground => ground;

        public bool IsInitialized => layout != null;

        public static CityCemeteryGroundExcavation Attach(
            GameObject host,
            CityLayout cityLayout,
            GameObject groundObject)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            var excavation =
                host.AddComponent<CityCemeteryGroundExcavation>();
            excavation.Initialize(cityLayout, groundObject);
            return excavation;
        }

        public void Initialize(
            CityLayout cityLayout,
            GameObject groundObject)
        {
            layout = cityLayout ??
                     throw new ArgumentNullException(
                         nameof(cityLayout));
            ground = groundObject;
            cuts.Clear();
        }

        /// <summary>
        /// Opens one hole and rebuilds the slab around it. False when
        /// the rectangle is degenerate, when it duplicates a hole
        /// already open, or when there is no slab to cut — a city
        /// whose blueprint carries no cemetery digs no graves.
        /// </summary>
        public bool Excavate(Rect mouth)
        {
            if (!IsInitialized ||
                ground == null ||
                mouth.width <= 0f ||
                mouth.height <= 0f ||
                !IsFinite(mouth))
            {
                return false;
            }

            for (int index = 0; index < cuts.Count; index++)
            {
                if (cuts[index].Overlaps(mouth))
                {
                    return false;
                }
            }

            cuts.Add(mouth);
            if (!Rebuild())
            {
                cuts.RemoveAt(cuts.Count - 1);
                return false;
            }

            return true;
        }

        private bool Rebuild()
        {
            Transform host = transform;
            int siblingIndex = ground.transform.GetSiblingIndex();
            GameObject rebuilt = CityCemeteryGroundWorldBuilder.Build(
                host,
                layout,
                cuts);
            if (rebuilt == null)
            {
                return false;
            }

            rebuilt.transform.SetSiblingIndex(siblingIndex);
            DestroyGameObject(ground);
            ground = rebuilt;
            return true;
        }

        private static void DestroyGameObject(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private static bool IsFinite(Rect value)
        {
            return !float.IsNaN(value.xMin) &&
                   !float.IsNaN(value.yMin) &&
                   !float.IsInfinity(value.xMax) &&
                   !float.IsInfinity(value.yMax);
        }
    }
}
