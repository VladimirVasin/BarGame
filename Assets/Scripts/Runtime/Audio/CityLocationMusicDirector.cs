using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One place in the city and the theme that belongs to it.
    /// </summary>
    public readonly struct CityLocationMusicSlot
    {
        public CityLocationMusicSlot(
            string locationId,
            Rect grounds,
            SceneMusicPlayer theme)
        {
            LocationId = locationId ?? string.Empty;
            Grounds = grounds;
            Theme = theme;
        }

        public string LocationId { get; }

        /// <summary>The world XZ footprint the theme belongs to.</summary>
        public Rect Grounds { get; }
        public SceneMusicPlayer Theme { get; }
    }

    /// <summary>
    /// Music that belongs to a place instead of to a scene. The City root
    /// keeps its own theme as the default and hands the mix over whenever the
    /// hero walks onto grounds that have a theme of their own. The handover
    /// itself is the shared rule, at the same length as any other music
    /// change: the theme being left reaches zero before the arriving one
    /// sounds.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    [DisallowMultipleComponent]
    public sealed class CityLocationMusicDirector : MonoBehaviour
    {
        public const string DefaultLocationId = "city";
        public const string CemeteryLocationId = "cemetery";

        private Transform hero;
        private SceneMusicPlayer defaultTheme;
        private CityLocationMusicSlot[] slots =
            Array.Empty<CityLocationMusicSlot>();
        private Rect[] grounds = Array.Empty<Rect>();
        private int activeIndex = CityLocationMusicZones.NoLocationIndex;
        private bool hasObservedLocation;

        public bool IsInitialized { get; private set; }
        public int SlotCount => slots.Length;
        public int ActiveLocationIndex => activeIndex;
        public IReadOnlyList<CityLocationMusicSlot> Slots => slots;

        public string ActiveLocationId =>
            activeIndex >= 0 && activeIndex < slots.Length
                ? slots[activeIndex].LocationId
                : DefaultLocationId;

        public SceneMusicPlayer ActiveTheme =>
            activeIndex >= 0 && activeIndex < slots.Length
                ? slots[activeIndex].Theme
                : defaultTheme;

        /// <summary>
        /// Builds the director beside the City theme, giving the cemetery its
        /// own player when the seed actually has a cemetery and the optional
        /// track is present.
        /// </summary>
        public static CityLocationMusicDirector Create(
            Transform parent,
            Transform heroTransform,
            SceneMusicPlayer cityTheme,
            CityCemeteryPlan cemeteryPlan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            var located = new List<CityLocationMusicSlot>();
            if (cemeteryPlan != null &&
                cemeteryPlan.Grounds.width > 0f &&
                cemeteryPlan.Grounds.height > 0f)
            {
                GameObject cemeteryObject =
                    new GameObject("Cemetery Music");
                cemeteryObject.transform.SetParent(parent, false);
                CemeteryMusicPlayer cemeteryTheme =
                    cemeteryObject.AddComponent<CemeteryMusicPlayer>();
                located.Add(
                    new CityLocationMusicSlot(
                        CemeteryLocationId,
                        cemeteryPlan.Grounds,
                        cemeteryTheme));
            }

            GameObject directorObject =
                new GameObject("City Location Music");
            directorObject.transform.SetParent(parent, false);
            CityLocationMusicDirector director =
                directorObject.AddComponent<CityLocationMusicDirector>();
            director.Initialize(heroTransform, cityTheme, located);
            return director;
        }

        public void Initialize(
            Transform heroTransform,
            SceneMusicPlayer sceneTheme,
            IReadOnlyList<CityLocationMusicSlot> locations)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The location music director is already initialized.");
            }

            if (heroTransform == null)
            {
                throw new ArgumentNullException(nameof(heroTransform));
            }

            if (sceneTheme == null)
            {
                throw new ArgumentNullException(nameof(sceneTheme));
            }

            hero = heroTransform;
            defaultTheme = sceneTheme;
            var accepted = new List<CityLocationMusicSlot>();
            for (int index = 0;
                 locations != null && index < locations.Count;
                 index++)
            {
                CityLocationMusicSlot slot = locations[index];
                if (slot.Theme == null)
                {
                    throw new ArgumentException(
                        "A location music slot requires a theme player.",
                        nameof(locations));
                }

                // An empty optional slot must not silence the city. Without
                // a track the place simply keeps the scene theme.
                if (slot.Theme.ActiveClip == null)
                {
                    continue;
                }

                // Parked from the start: a place theme is only ever heard
                // because the hero walked into it.
                slot.Theme.FadeOutAndPause(0f);
                accepted.Add(slot);
            }

            slots = accepted.ToArray();
            grounds = new Rect[slots.Length];
            for (int index = 0; index < slots.Length; index++)
            {
                grounds[index] = slots[index].Grounds;
            }

            activeIndex = CityLocationMusicZones.NoLocationIndex;
            hasObservedLocation = false;
            IsInitialized = true;
            RefreshLocation();
        }

        /// <summary>
        /// Reads where the hero is standing and hands the mix over when that
        /// changed. Returns true only when the observed place changed.
        /// </summary>
        public bool RefreshLocation()
        {
            if (!IsInitialized || hero == null || defaultTheme == null)
            {
                return false;
            }

            // A scene-exit fade owns every theme once requested. A late
            // position update must never revive one during teardown.
            if (IsAnyThemeLeavingTheScene())
            {
                return false;
            }

            Vector3 position = hero.position;
            int resolved = CityLocationMusicZones.Resolve(
                grounds,
                activeIndex,
                new Vector2(position.x, position.z));
            if (hasObservedLocation && resolved == activeIndex)
            {
                return false;
            }

            bool hadObservation = hasObservedLocation;
            SceneMusicPlayer leaving = ThemeAt(activeIndex);
            hasObservedLocation = true;
            activeIndex = resolved;
            SceneMusicPlayer arriving = ThemeAt(resolved);

            if (!hadObservation)
            {
                // Nothing has been established yet, so there is no handover
                // to hear: whatever the hero is standing in simply owns the
                // mix from the first frame.
                if (arriving != null && arriving != defaultTheme)
                {
                    defaultTheme.FadeOutAndPause(0f);
                    arriving.ResumeWithFadeIn(MusicMix.FadeInSeconds);
                }

                return true;
            }

            if (leaving != null)
            {
                leaving.FadeOutAndPause(MusicMix.FadeOutSeconds);
            }

            if (arriving != null)
            {
                arriving.ResumeWithFadeIn(MusicMix.FadeInSeconds);
            }

            return true;
        }

        private void Update()
        {
            RefreshLocation();
        }

        private SceneMusicPlayer ThemeAt(int index)
        {
            if (index < 0 || index >= slots.Length)
            {
                return defaultTheme != null ? defaultTheme : null;
            }

            SceneMusicPlayer theme = slots[index].Theme;
            return theme != null ? theme : null;
        }

        private bool IsAnyThemeLeavingTheScene()
        {
            if (defaultTheme != null &&
                defaultTheme.IsSceneExitFadeRequested)
            {
                return true;
            }

            for (int index = 0; index < slots.Length; index++)
            {
                SceneMusicPlayer theme = slots[index].Theme;
                if (theme != null && theme.IsSceneExitFadeRequested)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
