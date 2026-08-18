using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Takes the hero's own head off while a camera is sitting inside
    /// it, and puts it back afterwards.
    ///
    /// This exists because the head is not a mesh. On this rig it is
    /// twenty-two of them — the skull, the neck, a hair cap and
    /// fourteen tufts, two ears, a nose, stubble, two under-eye
    /// shadows, and then the face parts on their own little bones:
    /// eyes, pupils, brows and a mouth. Hiding "the head renderer"
    /// hides the skull and leaves the player looking at the inside of
    /// his own hair.
    ///
    /// So the rule is stated against the rig rather than against a
    /// list of meshes: anything weighted to `head`, to `neck` or to any
    /// `face.*` bone is head geometry and comes off together. A part
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
                       StringComparison.OrdinalIgnoreCase);
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
                    !binding.Renderer.enabled ||
                    !IsHeadGeometry(binding.BoneName))
                {
                    continue;
                }

                binding.Renderer.enabled = false;
                visibility.hidden.Add(binding.Renderer);
            }

            return visibility;
        }

        public void Restore()
        {
            for (int index = 0; index < hidden.Count; index++)
            {
                Renderer renderer = hidden[index];
                if (renderer != null)
                {
                    renderer.enabled = true;
                }
            }

            hidden.Clear();
        }
    }
}
