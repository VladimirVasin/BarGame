using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>
    /// Drives the cemetery mourner's pose: MournerWalk while the
    /// controller moves her along her route, and one playback of the
    /// MournerMourn rite (lay, sob, wipe) while she stands at the
    /// grave — through a small manual PlayableGraph exactly like the
    /// drying-yard babushkas. The staged prefab ships the bouquet in
    /// her hands; the controller hides it at the lay cue and stands
    /// its own bouquet on the grave. The presentation stays passive:
    /// the mourner controller owns spawning, movement and despawn.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CemeteryMournerPresentation : MonoBehaviour
    {
        /// <summary>A hitch longer than this advances the loop by a
        /// bounded step instead of teleporting mid-stride.</summary>
        public const float MaximumStepSeconds = 0.1f;

        private static readonly string[] BouquetRendererNames =
        {
            "ACC_BouquetStems",
            "ACC_BouquetWrap",
            "ACC_BouquetBloomA",
            "ACC_BouquetBloomB",
            "ACC_BouquetGreens"
        };

        private PlayableGraph graph;
        private AnimationClipPlayable playable;
        private AnimationPlayableOutput output;
        private bool hasGraph;

        public bool IsInitialized { get; private set; }
        public CityPedestrianAssetRegistry Registry { get; private set; }
        public AnimationClip ActiveClip { get; private set; }

        public void Initialize(
            CityPedestrianAssetRegistry registry,
            int paletteVariant)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (registry.Animator == null)
            {
                throw new InvalidOperationException(
                    "The mourner prefab has no Animator.");
            }

            // Walk slot carries the grieving gait, idle slot the whole
            // graveside rite — the same mapping the art build declares.
            if (registry.WalkClip == null || registry.IdleClip == null)
            {
                throw new InvalidOperationException(
                    "The mourner prefab is missing its authored loops.");
            }

            Registry = registry;
            registry.ApplyPaletteVariant(paletteVariant);

            graph = PlayableGraph.Create("Cemetery Mourner");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            output = AnimationPlayableOutput.Create(
                graph,
                "Cemetery Mourner Pose",
                registry.Animator);
            graph.Play();
            hasGraph = true;
            Play(registry.WalkClip);
            graph.Evaluate(0f);
            IsInitialized = true;
        }

        public void PlayWalk()
        {
            Play(Registry.WalkClip);
        }

        /// <summary>Starts the single graveside playback: lay the
        /// flowers, thirty seconds of sobbing, wipe the tears.</summary>
        public void PlayMournRite()
        {
            Play(Registry.IdleClip);
        }

        /// <summary>The bouquet has left her hands: from here on she
        /// walks and grieves empty-handed.</summary>
        public void HideHeldBouquet()
        {
            for (int index = 0;
                 index < Registry.Renderers.Count;
                 index++)
            {
                Renderer renderer = Registry.Renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                for (int name = 0;
                     name < BouquetRendererNames.Length;
                     name++)
                {
                    if (string.Equals(
                            renderer.name,
                            BouquetRendererNames[name],
                            StringComparison.Ordinal))
                    {
                        renderer.enabled = false;
                        break;
                    }
                }
            }
        }

        private void Play(AnimationClip clip)
        {
            if (!hasGraph || clip == null || ActiveClip == clip)
            {
                return;
            }

            if (playable.IsValid())
            {
                playable.Destroy();
            }

            playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetTime(0.0);
            output.SetSourcePlayable(playable);
            ActiveClip = clip;
        }

        private void LateUpdate()
        {
            if (!hasGraph)
            {
                return;
            }

            graph.Evaluate(
                Mathf.Min(Time.deltaTime, MaximumStepSeconds));
        }

        private void OnDestroy()
        {
            if (hasGraph && graph.IsValid())
            {
                graph.Destroy();
            }

            hasGraph = false;
        }
    }
}
