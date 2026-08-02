using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum HomeOccluderKind
    {
        VisualRail,
        FurnitureBlocker,
        SoftDecoration,
        StructuralCutaway,
        InteractiveProtected
    }

    /// <summary>
    /// Stable runtime description of one logical Home presentation group.
    /// Rendering state is deliberately owned by the occlusion controller.
    /// </summary>
    public sealed class HomeOccluderGroup
    {
        private readonly List<Renderer> renderers =
            new List<Renderer>();
        private readonly ReadOnlyCollection<Renderer>
            readOnlyRenderers;

        internal HomeOccluderGroup(
            string id,
            HomeOccluderKind kind,
            float minimumVisibility)
        {
            Id = id;
            Kind = kind;
            MinimumVisibility = minimumVisibility;
            readOnlyRenderers = renderers.AsReadOnly();
        }

        public string Id { get; }

        public HomeOccluderKind Kind { get; }

        public float MinimumVisibility { get; }

        public IReadOnlyList<Renderer> Renderers =>
            readOnlyRenderers;

        internal void Add(Renderer renderer)
        {
            renderers.Add(renderer);
        }
    }

    /// <summary>
    /// Explicit data-first registry for visual objects which may reveal the
    /// Home player. It never searches the scene by name and never mutates a
    /// renderer or material.
    /// </summary>
    public sealed class HomeOcclusionRegistry : MonoBehaviour
    {
        private readonly List<HomeOccluderGroup> groups =
            new List<HomeOccluderGroup>();
        private readonly Dictionary<string, HomeOccluderGroup>
            groupsById =
                new Dictionary<string, HomeOccluderGroup>(
                    StringComparer.Ordinal);
        private readonly Dictionary<Renderer, HomeOccluderGroup>
            ownersByRenderer =
                new Dictionary<Renderer, HomeOccluderGroup>();
        private ReadOnlyCollection<HomeOccluderGroup>
            readOnlyGroups;

        public IReadOnlyList<HomeOccluderGroup> Groups
        {
            get
            {
                if (readOnlyGroups == null)
                {
                    readOnlyGroups = groups.AsReadOnly();
                }

                return readOnlyGroups;
            }
        }

        public HomeOccluderGroup Register(
            string id,
            HomeOccluderKind kind,
            float minimumVisibility,
            params Renderer[] renderers)
        {
            ValidateNewGroup(id, kind, minimumVisibility);
            ValidateRendererSources(renderers);

            HomeOccluderGroup group =
                new HomeOccluderGroup(
                    id,
                    kind,
                    minimumVisibility);
            AddValidatedRenderers(group, renderers);
            CompleteRegistration(group);
            return group;
        }

        public HomeOccluderGroup Register(
            string id,
            HomeOccluderKind kind,
            float minimumVisibility,
            params GameObject[] objects)
        {
            ValidateNewGroup(id, kind, minimumVisibility);
            Renderer[] renderers = CollectRenderers(objects);

            HomeOccluderGroup group =
                new HomeOccluderGroup(
                    id,
                    kind,
                    minimumVisibility);
            AddValidatedRenderers(group, renderers);
            CompleteRegistration(group);
            return group;
        }

        public void AddRenderers(
            string id,
            params Renderer[] renderers)
        {
            HomeOccluderGroup group = GetGroupOrThrow(id);
            ValidateRendererSources(renderers);
            AddValidatedRenderers(group, renderers);
        }

        public void AddRenderers(
            string id,
            params GameObject[] objects)
        {
            HomeOccluderGroup group = GetGroupOrThrow(id);
            Renderer[] renderers = CollectRenderers(objects);
            AddValidatedRenderers(group, renderers);
        }

        public bool TryGetGroup(
            string id,
            out HomeOccluderGroup group)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                group = null;
                return false;
            }

            return groupsById.TryGetValue(id, out group);
        }

        private void ValidateNewGroup(
            string id,
            HomeOccluderKind kind,
            float minimumVisibility)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "An occluder group id is required.",
                    nameof(id));
            }

            if (float.IsNaN(minimumVisibility) ||
                minimumVisibility < 0f ||
                minimumVisibility > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumVisibility),
                    "Minimum visibility must be between zero and one.");
            }

            if (!Enum.IsDefined(typeof(HomeOccluderKind), kind))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    "The Home occluder kind is not defined.");
            }

            if (groupsById.ContainsKey(id))
            {
                throw new InvalidOperationException(
                    $"Home occluder group '{id}' is already registered.");
            }
        }

        private static Renderer[] CollectRenderers(
            GameObject[] objects)
        {
            if (objects == null)
            {
                throw new ArgumentNullException(nameof(objects));
            }

            List<Renderer> renderers = new List<Renderer>();
            for (int index = 0; index < objects.Length; index++)
            {
                GameObject source = objects[index];
                if (source == null)
                {
                    throw new ArgumentException(
                        "Occluder objects cannot contain null.",
                        nameof(objects));
                }

                Renderer[] descendants =
                    source.GetComponentsInChildren<Renderer>(true);
                renderers.AddRange(descendants);
            }

            Renderer[] result = renderers.ToArray();
            ValidateRendererSources(result);
            return result;
        }

        private static void ValidateRendererSources(
            Renderer[] renderers)
        {
            if (renderers == null)
            {
                throw new ArgumentNullException(nameof(renderers));
            }

            if (renderers.Length == 0)
            {
                throw new ArgumentException(
                    "At least one renderer is required.",
                    nameof(renderers));
            }

            for (int index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] == null)
                {
                    throw new ArgumentException(
                        "Occluder renderers cannot contain null.",
                        nameof(renderers));
                }
            }
        }

        private void AddValidatedRenderers(
            HomeOccluderGroup group,
            Renderer[] renderers)
        {
            HashSet<Renderer> incoming =
                new HashSet<Renderer>();
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (!incoming.Add(renderer))
                {
                    continue;
                }

                if (ownersByRenderer.TryGetValue(
                        renderer,
                        out HomeOccluderGroup owner))
                {
                    if (ReferenceEquals(owner, group))
                    {
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"Renderer '{renderer.name}' already belongs to " +
                        $"Home occluder group '{owner.Id}'.");
                }
            }

            foreach (Renderer renderer in incoming)
            {
                if (ownersByRenderer.ContainsKey(renderer))
                {
                    continue;
                }

                group.Add(renderer);
                ownersByRenderer.Add(renderer, group);
            }
        }

        private void CompleteRegistration(
            HomeOccluderGroup group)
        {
            if (group.Renderers.Count == 0)
            {
                throw new ArgumentException(
                    "At least one unique renderer is required.");
            }

            groups.Add(group);
            groupsById.Add(group.Id, group);
        }

        private HomeOccluderGroup GetGroupOrThrow(
            string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "An occluder group id is required.",
                    nameof(id));
            }

            if (!groupsById.TryGetValue(
                    id,
                    out HomeOccluderGroup group))
            {
                throw new KeyNotFoundException(
                    $"Home occluder group '{id}' is not registered.");
            }

            return group;
        }
    }
}
