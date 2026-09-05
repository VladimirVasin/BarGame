using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Attach the small optional bank to this live hero, preserving its prefab.</summary>
    public static class ChurchGardenPotActions
    {
        public const string ResourcePath = "Player/ChurchGardenPotActions";

        public static readonly string[] RequiredClipNames =
        {
            "ChurchPotPickupLeft", "ChurchPotPickupRight", "ChurchPotInspectLoop",
            "ChurchPotPlaceLeft", "ChurchPotPlaceRight"
        };

        public static bool TryAttach(Player3DAssetRegistry registry)
        {
            if (registry == null)
            {
                return false;
            }

            AnimationClip[] clips = Resources.LoadAll<AnimationClip>(ResourcePath);
            var bindings = new List<Player3DAnimationBinding>(registry.Animations);
            foreach (string name in RequiredClipNames)
            {
                if (registry.TryGetAnimation(name, out _))
                {
                    continue;
                }

                AnimationClip clip = clips.FirstOrDefault(candidate => candidate.name == name);
                bool loop = name == "ChurchPotInspectLoop";
                float duration = loop ? 5f : 3f;
                if (clip == null || clip.isLooping != loop || clip.events.Length != 0 ||
                    Mathf.Abs(clip.length - duration) > 0.003f)
                {
                    return false;
                }

                bindings.Add(new Player3DAnimationBinding(name, "church_garden", clip, duration, loop));
            }

            registry.Configure(
                registry.Animator, registry.ModelRoot,
                registry.Renderers.ToArray(), registry.MeshBindings.ToArray(),
                registry.AnatomicalParts.ToArray(), bindings.ToArray(),
                registry.Anchors, registry.Metrics, registry.SourceGeneratorVersion,
                registry.SourcePose, registry.SourceTriangleCount, registry.BuildSignature,
                registry.FaceAtlas);
            return true;
        }
    }
}
