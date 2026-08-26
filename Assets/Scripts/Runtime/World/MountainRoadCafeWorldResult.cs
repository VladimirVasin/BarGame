using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class MountainRoadCafeWorldResult
    {
        internal MountainRoadCafeWorldResult(
            MountainRoadCafePlan plan,
            GameObject root,
            GameObject physicalRoot,
            GameObject dressingRoot,
            GameObject npcRoot,
            GameObject lightingRoot,
            MountainRoadCafeSoundscape soundscape,
            IList<Light> sourceLights,
            IDictionary<string, Transform> sourceSemanticAnchors,
            MountainRoadCafeCastController cast)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            Root = root ?? throw new ArgumentNullException(nameof(root));
            PhysicalRoot = physicalRoot ??
                throw new ArgumentNullException(nameof(physicalRoot));
            DressingRoot = dressingRoot ??
                throw new ArgumentNullException(nameof(dressingRoot));
            NpcRoot = npcRoot ??
                throw new ArgumentNullException(nameof(npcRoot));
            LightingRoot = lightingRoot ??
                throw new ArgumentNullException(nameof(lightingRoot));
            Soundscape = soundscape ??
                throw new ArgumentNullException(nameof(soundscape));
            Lights = new ReadOnlyCollection<Light>(
                new List<Light>(sourceLights));
            Sources = new ReadOnlyCollection<AudioSource>(
                new List<AudioSource>(soundscape.Sources));
            RuntimeClips = new ReadOnlyCollection<AudioClip>(
                new List<AudioClip>(soundscape.RuntimeClips));
            Cast = cast;
            SemanticAnchors =
                new ReadOnlyDictionary<string, Transform>(
                    new Dictionary<string, Transform>(
                        sourceSemanticAnchors,
                        StringComparer.Ordinal));
        }

        public MountainRoadCafePlan Plan { get; }

        /// <summary>
        /// The four silent ones and their scheduler. Handed out so the
        /// counter stool can ask the attendant to notice somebody sitting
        /// down at it - the one thing in this room that answers a reason
        /// rather than a timer.
        /// </summary>
        public MountainRoadCafeCastController Cast { get; }
        public GameObject Root { get; }
        public GameObject PhysicalRoot { get; }
        public GameObject DressingRoot { get; }
        public GameObject NpcRoot { get; }
        public GameObject LightingRoot { get; }
        public MountainRoadCafeSoundscape Soundscape { get; }
        public IReadOnlyList<Light> Lights { get; }
        public IReadOnlyList<AudioSource> Sources { get; }
        public IReadOnlyList<AudioClip> RuntimeClips { get; }
        public IReadOnlyDictionary<string, Transform> SemanticAnchors { get; }

        public Transform Entrance =>
            SemanticAnchors[MountainRoadCafeWorldBuilder.EntranceAnchorId];

        public bool ContainsInterior(Vector3 worldPosition)
        {
            return Plan.ContainsInterior(worldPosition);
        }
    }
}
