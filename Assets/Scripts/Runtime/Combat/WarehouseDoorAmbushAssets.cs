using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One cached, measured paired bank; no new hero or enemy appearance.</summary>
    internal static class WarehouseDoorAmbushAssets
    {
        internal const string Enter = "DoorAmbushEnter", Hold = "DoorAmbushHold", Exit = "DoorAmbushExit";
        internal const string Attack = "CraneDoorAmbush", CraneHold = "CraneDoorHold", Retreat = "CraneDoorRetreat";
        private static readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>(6);
        private static Manifest manifest;
        private static GameObject guide;

        [Serializable] internal sealed class Manifest
        {
            public float attack_cue_seconds, capture_seconds;
            public float[] hero_neutral_pelvis, hero_action_pelvis, hero_exit_pelvis, hero_door_grip, crane_root_offset;
        }

        internal static Manifest Plan
        {
            get
            {
                if (manifest != null) return manifest;
                TextAsset source = Resources.Load<TextAsset>("CombatDoorAmbush/DoorAmbush3D");
                if (source == null) throw new InvalidOperationException("Missing warehouse door ambush manifest.");
                Manifest next = JsonUtility.FromJson<Manifest>(source.text);
                if (next == null || Mathf.Abs(next.attack_cue_seconds - 3f) > .0001f ||
                    Mathf.Abs(next.capture_seconds - 8f) > .0001f)
                    throw new InvalidOperationException("Invalid warehouse door ambush timing.");
                Vector(next.hero_neutral_pelvis); Vector(next.hero_action_pelvis); Vector(next.hero_exit_pelvis);
                Vector(next.hero_door_grip); Vector(next.crane_root_offset);
                return manifest = next;
            }
        }

        internal static Vector3 Vector(float[] data)
        {
            if (data == null || data.Length != 3 || !float.IsFinite(data[0]) || !float.IsFinite(data[1]) || !float.IsFinite(data[2]))
                throw new InvalidOperationException("Ambush anchors must be finite authored metre vectors.");
            return new Vector3(data[0], data[1], data[2]);
        }

        internal static AnimationClip Clip(string name)
        {
            if (clips.Count == 0)
            {
                LoadBank("HeroActions", new[] { Enter, Hold, Exit }, new[] { 8f, 1f, .65f });
                LoadBank("CraneActions", new[] { Attack, CraneHold, Retreat }, new[] { 5f, 1f, 1.2f });
            }
            return clips.TryGetValue(name, out AnimationClip result) ? result :
                throw new ArgumentException("Unknown door ambush action.", nameof(name));
        }

        private static void LoadBank(string bank, string[] names, float[] durations)
        {
            foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>("CombatDoorAmbush/" + bank))
            {
                int index = Array.IndexOf(names, clip.name);
                if (index < 0) continue;
                if (clip.events.Length != 0 || clip.isLooping != (index == 1) || Mathf.Abs(clip.length - durations[index]) > .003f)
                    throw new InvalidOperationException("Invalid authored door ambush clip: " + clip.name);
                clips.Add(clip.name, clip);
            }
            foreach (string name in names) if (!clips.ContainsKey(name)) throw new InvalidOperationException("Missing door ambush action: " + name);
        }

        internal static GameObject CreateGuides(Transform parent, out Transform[] anchors)
        {
            if (guide == null) guide = Resources.Load<GameObject>("CombatDoorAmbush/CraneDoorCableGuide");
            if (guide == null) throw new InvalidOperationException("Missing authored cable fairleads.");
            GameObject host = UnityEngine.Object.Instantiate(guide, parent, false);
            host.name = "Door ambush cable fairleads";
            // Keep the imported root's unit conversion; only the metre wrapper moves.
            host.transform.localPosition = new Vector3(0f, 2.94f, 5.94f);
            Material metal = CombatCraneAssetProvider.LoadBodyMaterial("steel");
            foreach (MeshRenderer renderer in host.GetComponentsInChildren<MeshRenderer>(true)) renderer.sharedMaterial = metal;
            anchors = new Transform[8];
            for (int i = 0; i < 6; i++)
                anchors[i] = CityPedestrianHandProps.FindSocket(host.transform, "ANCHOR_Guide" + i)
                    ?? throw new InvalidOperationException("Missing cable guide anchor " + i);
            for (int i = 0; i < 2; i++)
                anchors[6 + i] = CityPedestrianHandProps.FindSocket(host.transform, "ANCHOR_NeckGuide" + i)
                    ?? throw new InvalidOperationException("Missing roof cable guide anchor " + i);
            return host;
        }
    }
}
