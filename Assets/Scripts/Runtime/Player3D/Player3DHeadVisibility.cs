using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Takes the hero's own head off while a camera is sitting inside
    /// it, and puts it back afterwards.
    ///
    /// The skull, neck, face, fixed hair cap and moving hair locks are
    /// independently drawn. Hiding only the skull would leave the
    /// player looking at the inside of his own hair.
    ///
    /// So the rule is stated against the rig rather than against a
    /// list of meshes: anything weighted to `head`, to `neck`, to any
    /// `face.*` bone or an authored hair chain comes off together. A part
    /// added to the model later lands on one of those bones or it does
    /// not, and either way this keeps being right.
    ///
    /// Renderers that were already off are left alone, so restoring
    /// never switches on something somebody else had switched off.
    /// </summary>
    public sealed class Player3DHeadVisibility
    {
        public const string HeadBoneName = "head";
        public const string NeckBoneName = "neck";
        public const string FaceBonePrefix = "face.";

        private readonly List<Renderer> hidden = new List<Renderer>(24);
        private readonly List<Renderer> ownedSources = new List<Renderer>();
        private static readonly HashSet<Renderer> temporarilyHidden = new HashSet<Renderer>();
        private static readonly Dictionary<Renderer, List<Renderer>> derived = new Dictionary<Renderer, List<Renderer>>();
        private static readonly Dictionary<Renderer, bool> derivedVisibility = new Dictionary<Renderer, bool>();
        private static readonly Dictionary<Renderer, Player3DHeadVisibility> hideOwners = new Dictionary<Renderer, Player3DHeadVisibility>();
        internal static bool IsTemporarilyHidden(Renderer renderer) => temporarilyHidden.Contains(renderer);

        internal static void RegisterDerived(Renderer source, Renderer renderer)
        {
            if (!derived.TryGetValue(source, out List<Renderer> surfaces)) derived.Add(source, surfaces = new List<Renderer>());
            surfaces.Add(renderer);
            derivedVisibility.Add(renderer, renderer.enabled);
        }

        internal static void UnregisterDerived(Renderer source, Renderer renderer)
        {
            if (derived.TryGetValue(source, out List<Renderer> surfaces))
            {
                surfaces.Remove(renderer);
                if (surfaces.Count == 0) derived.Remove(source);
            }
            temporarilyHidden.Remove(renderer); hideOwners.Remove(renderer);
            derivedVisibility.Remove(renderer);
        }

        internal static void SetDerivedEnabled(Renderer source, Renderer renderer, bool enabled)
        {
            renderer.enabled = enabled;
            derivedVisibility[renderer] = enabled;
            if (enabled && hideOwners.TryGetValue(source, out Player3DHeadVisibility owner)) owner.HideRenderer(renderer);
        }

        private void HideRenderer(Renderer renderer)
        {
            if (temporarilyHidden.Contains(renderer)) { renderer.enabled = false; return; }
            if (!renderer.enabled) return;
            renderer.enabled = false; hidden.Add(renderer); temporarilyHidden.Add(renderer); hideOwners[renderer] = this;
        }

        private Player3DHeadVisibility()
        {
        }

        /// <summary>How many renderers this actually switched off.</summary>
        public int HiddenRendererCount => hidden.Count;

        /// <summary>
        /// Whether a mesh weighted to this bone is part of the head.
        /// Pure, so the classification can be checked against the real
        /// model rather than assumed.
        /// </summary>
        public static bool IsHeadGeometry(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                return false;
            }

            return string.Equals(
                       boneName,
                       HeadBoneName,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       boneName,
                       NeckBoneName,
                       StringComparison.OrdinalIgnoreCase) ||
                   boneName.StartsWith(
                       FaceBonePrefix,
                       StringComparison.OrdinalIgnoreCase) ||
                   IsHairGeometry(boneName);
        }

        public static bool IsHairGeometry(string boneName) =>
            !string.IsNullOrEmpty(boneName) &&
            (boneName.StartsWith("HairBack.", StringComparison.OrdinalIgnoreCase) ||
             boneName.StartsWith("HairLeft.", StringComparison.OrdinalIgnoreCase) ||
             boneName.StartsWith("HairRight.", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Whether the hero's head is currently on screen at all. Every
        /// first-person view in the game — the counter stool, the cafe seat,
        /// the car, the cableway cabin, the board game — takes it off through
        /// <see cref="Hide"/>, so one question answers all of them without the
        /// asker having to know any of them by name. Anything that hangs over
        /// his head has to ask it first.
        /// </summary>
        public static bool IsHeadDrawn(Player3DAssetRegistry registry)
        {
            if (registry == null)
            {
                return false;
            }

            IReadOnlyList<Player3DMeshBinding> bindings =
                registry.MeshBindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                Player3DMeshBinding binding = bindings[index];
                if (binding != null &&
                    binding.Renderer != null &&
                    binding.Renderer.enabled &&
                    IsHeadGeometry(binding.BoneName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Switches off every head renderer on one rig and returns the
        /// handle that puts them back. Never null: a rig with no head
        /// bindings simply hides nothing.
        /// </summary>
        public static Player3DHeadVisibility Hide(
            Player3DAssetRegistry registry)
        {
            var visibility = new Player3DHeadVisibility();
            if (registry == null)
            {
                return visibility;
            }

            IReadOnlyList<Player3DMeshBinding> bindings =
                registry.MeshBindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                Player3DMeshBinding binding = bindings[index];
                if (binding == null ||
                    binding.Renderer == null ||
                    !IsHeadGeometry(binding.BoneName))
                {
                    continue;
                }

                if (!hideOwners.ContainsKey(binding.Renderer))
                {
                    hideOwners.Add(binding.Renderer, visibility);
                    visibility.ownedSources.Add(binding.Renderer);
                }
                visibility.HideRenderer(binding.Renderer);
                if (derived.TryGetValue(binding.Renderer, out List<Renderer> surfaces))
                    foreach (Renderer renderer in surfaces) if (renderer != null) visibility.HideRenderer(renderer);
            }

            return visibility;
        }

        public void Restore()
        {
            for (int index = 0; index < hidden.Count; index++)
            {
                Renderer renderer = hidden[index];
                temporarilyHidden.Remove(renderer);
                hideOwners.Remove(renderer);
                if (renderer != null && !CombatHeadDestruction.IsSuppressed(renderer) &&
                    (!derivedVisibility.TryGetValue(renderer, out bool enabled) || enabled))
                {
                    renderer.enabled = true;
                }
            }

            hidden.Clear();
            foreach (Renderer source in ownedSources)
                if (hideOwners.TryGetValue(source, out Player3DHeadVisibility owner) && owner == this) hideOwners.Remove(source);
            ownedSources.Clear();
        }
    }
}
