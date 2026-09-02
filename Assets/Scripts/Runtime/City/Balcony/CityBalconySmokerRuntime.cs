using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Scene-owned lifetime handle for the small balcony smoker cast.
    /// Visibility pauses the disabled presentations without changing their
    /// deterministic phases; Shutdown destroys their manual playable graphs
    /// before removing the runtime root.
    /// </summary>
    public sealed class CityBalconySmokerRuntime
    {
        private readonly List<CityBalconySmokerPresentation> mutable;
        private readonly ReadOnlyCollection<
            CityBalconySmokerPresentation> presentations;

        internal CityBalconySmokerRuntime(
            GameObject rootGameObject,
            IList<CityBalconySmokerPresentation> source)
        {
            RootGameObject = rootGameObject != null
                ? rootGameObject
                : throw new ArgumentNullException(nameof(rootGameObject));
            mutable = new List<CityBalconySmokerPresentation>(
                source ??
                throw new ArgumentNullException(nameof(source)));
            presentations = new ReadOnlyCollection<
                CityBalconySmokerPresentation>(mutable);
        }

        public GameObject RootGameObject { get; private set; }
        public IReadOnlyList<CityBalconySmokerPresentation> Presentations =>
            presentations;
        public int Count => mutable.Count;
        public bool IsPresent => Count > 0;
        public bool IsVisible =>
            RootGameObject != null && RootGameObject.activeSelf;

        public void SetVisible(bool visible)
        {
            if (RootGameObject != null &&
                RootGameObject.activeSelf != visible)
            {
                RootGameObject.SetActive(visible);
            }
        }

        public void Shutdown()
        {
            for (int index = 0; index < mutable.Count; index++)
            {
                CityBalconySmokerPresentation presentation = mutable[index];
                if (presentation != null)
                {
                    presentation.Shutdown();
                }
            }

            mutable.Clear();
            if (RootGameObject != null)
            {
                CityPedestrianResources.DestroyObject(RootGameObject);
                RootGameObject = null;
            }
        }
    }
}
