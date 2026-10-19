using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Finite authored Room gestures, planted idle, tableau and positional laugh.</summary>
    public static class CombatRoomAttackAssetProvider
    {
        public const string PointHeroClip = "RoomPointHero", PointChestClip = "RoomPointChest",
            LaughClip = "RoomLaugh", IdleClip = "RoomIdle", HeadPainClip = "RoomHeadPain", PointingShape = "RoomPoint";
        public static readonly string[] NpcClipNames = { PointHeroClip, PointChestClip, LaughClip, IdleClip };
        private static readonly Dictionary<string, AnimationClip> Clips = new Dictionary<string, AnimationClip>(5);
        private static GameObject tableau;
        private static AudioClip laughter;
        private static Material plaster, wood;
        private static Material[] plasterSlots, woodSlots;

        public static float ClipDuration(string name) => name switch
        {
            PointHeroClip => 1f, HeadPainClip => 2f, PointChestClip => .45f, LaughClip or IdleClip => 5f,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown Room action.")
        };

        public static AnimationClip LoadNpcClip(string name)
        {
            if (Array.IndexOf(NpcClipNames, name) < 0) throw new ArgumentOutOfRangeException(nameof(name));
            return LoadClip("Combat/RoomAttackNpcActions", name);
        }

        public static AnimationClip LoadHeroClip(string name)
        {
            if (name != HeadPainClip) throw new ArgumentOutOfRangeException(nameof(name));
            return LoadClip("Player/RoomAttackHeroActions", name);
        }

        private static AnimationClip LoadClip(string resource, string name)
        {
            if (Clips.TryGetValue(name, out AnimationClip result) && result != null) return result;
            foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>(resource))
            {
                if (clip.name != name) continue;
                if (clip.events.Length != 0 || clip.isLooping != (name == IdleClip) || Mathf.Abs(clip.length - ClipDuration(name)) > .003f)
                    throw new InvalidOperationException("Invalid in-place Room action: " + name);
                Clips[name] = clip;
                return clip;
            }
            throw new InvalidOperationException("Missing authored Room action: " + name);
        }

        public static GameObject LoadTableau() => tableau != null ? tableau : tableau =
            Resources.Load<GameObject>("Combat/RoomAttackTableau") ?? throw new InvalidOperationException("Missing authored Room tableau.");

        public static AudioClip LoadLaughAudio()
        {
            if (laughter == null) laughter = Resources.Load<AudioClip>("Combat/RoomLaugh");
            if (laughter == null || Mathf.Abs(laughter.length - 5f) > .003f)
                throw new InvalidOperationException("Missing five-second Room laugh.");
            return laughter;
        }

        public static void ApplyTableauMaterials(GameObject instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            plaster ??= CombatRoomAssetProvider.LoadBodyMaterial("plaster");
            wood ??= CreateMaterial("RoomAttackWood");
            plasterSlots ??= new[] { plaster };
            woodSlots ??= new[] { wood };
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                renderer.sharedMaterials = renderer.name.Contains("Wood", StringComparison.Ordinal) ? woodSlots : plasterSlots;
        }

        private static Material CreateMaterial(string texture)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? throw new InvalidOperationException("Missing shared URP surface shader.");
            var map = Resources.Load<Texture2D>("Combat/" + texture)
                ?? throw new InvalidOperationException("Missing Room surface: " + texture);
            var material = new Material(shader) { name = texture + " Shared", hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            material.SetTexture("_BaseMap", map);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", .08f);
            material.SetFloat("_Metallic", 0f);
            return material;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            Clips.Clear(); tableau = null; laughter = null;
            if (wood != null) UnityEngine.Object.Destroy(wood);
            plaster = wood = null;
            plasterSlots = woodSlots = null;
        }
    }
}
