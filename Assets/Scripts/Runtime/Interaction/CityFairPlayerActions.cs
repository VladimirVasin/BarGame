using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BarPromenade
{
    public enum CityFairInteractionKind { Organ, Bell }

    /// <summary>Optional authored actions on the unchanged production hero.</summary>
    public static class CityFairPlayerActions
    {
        public const string ResourcePath = "Player/CityFairPlayerActions";
        public const float TransferSeconds = 1.25f;
        public static readonly string[] RequiredClipNames =
            { "FairOrganEnter", "FairOrganLoop", "FairOrganExit", "FairBellEnter", "FairBellLoop", "FairBellExit" };
        public static float LoopSeconds(CityFairInteractionKind kind) => kind == CityFairInteractionKind.Organ ? 4f : 2f;
        public static string ClipName(CityFairInteractionKind kind, string phase) => "Fair" + kind + phase;
        public static PlayerAnimatedInteractionDefinition Definition(CityFairInteractionKind kind) =>
            new PlayerAnimatedInteractionDefinition(ClipName(kind, "Enter"), ClipName(kind, "Loop"), ClipName(kind, "Exit"),
                enterFrameCount: 30, enterFramesPerSecond: 24f,
                loopFrameCount: Mathf.RoundToInt(LoopSeconds(kind) * 24f), loopFramesPerSecond: 24f,
                exitFrameCount: 30, exitFramesPerSecond: 24f);

        public static float CrankDegrees(float progress) => 720f * Smooth(progress);
        public static float RopeDrop(float progress)
        {
            float wave = Mathf.Sin(Mathf.PI * Mathf.Clamp01(progress));
            return .24f * wave * wave;
        }
        public static Vector3 RightGripFromGround(CityFairInteractionKind kind, float progress)
        {
            if (kind == CityFairInteractionKind.Bell) return new Vector3(.22f, 1.13f - RopeDrop(progress), .52f);
            float angle = CrankDegrees(progress) * Mathf.Deg2Rad;
            return new Vector3(.22f + .18f * Mathf.Sin(angle), 1.10f + .18f * Mathf.Cos(angle), .50f);
        }
        public static float Smooth(float value) { value = Mathf.Clamp01(value); return value * value * (3f - 2f * value); }

        public static bool TryAttach(Player3DAssetRegistry registry)
        {
            if (registry == null) return false;
            AnimationClip[] clips = Resources.LoadAll<AnimationClip>(ResourcePath);
            var bindings = new List<Player3DAnimationBinding>(registry.Animations);
            foreach (string name in RequiredClipNames)
            {
                if (registry.TryGetAnimation(name, out _)) continue;
                AnimationClip clip = clips.FirstOrDefault(candidate => candidate.name == name);
                bool loop = name.EndsWith("Loop", StringComparison.Ordinal);
                float duration = loop ? (name.Contains("Organ") ? 4f : 2f) : TransferSeconds;
                if (clip == null || clip.isLooping != loop || clip.events.Length != 0 ||
                    Mathf.Abs(clip.length - duration) > .003f) return false;
                bindings.Add(new Player3DAnimationBinding(name, "city_fair", clip, duration, loop));
            }
            registry.Configure(registry.Animator, registry.ModelRoot, registry.Renderers.ToArray(),
                registry.MeshBindings.ToArray(), registry.AnatomicalParts.ToArray(), bindings.ToArray(),
                registry.Anchors, registry.Metrics, registry.SourceGeneratorVersion, registry.SourcePose,
                registry.SourceTriangleCount, registry.BuildSignature, registry.FaceAtlas);
            return true;
        }
    }
}
