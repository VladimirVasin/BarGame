using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Author a preset or a mixed outfit on the existing NPC rig.</summary>
    [CustomEditor(typeof(NpcWardrobe), true)]
    public sealed class NpcWardrobeEditor : UnityEditor.Editor
    {
        private string error;

        public override void OnInspectorGUI()
        {
            var wardrobe = (NpcWardrobe)target;
            if (!wardrobe.IsModular)
            {
                DrawDefaultInspector();
                return;
            }
            EditorGUILayout.LabelField("Modular NPC wardrobe", EditorStyles.boldLabel);
            var appearance = wardrobe.GetComponent<DefaultNpcAppearance>();
            if (appearance != null)
            {
                string[] faces = appearance.Faces.Select(face => face.Id).ToArray();
                int currentFace = Array.IndexOf(faces, appearance.CurrentFaceId);
                int nextFace = EditorGUILayout.Popup("Face", Mathf.Max(0, currentFace), faces);
                if (nextFace != Mathf.Max(0, currentFace))
                    Change(wardrobe, () => appearance.ApplyFace(faces[nextFace]));
                string[] hairColors = appearance.HairColors.Select(hair => hair.Id).ToArray();
                int currentHair = Array.IndexOf(hairColors, appearance.CurrentHairColorId);
                int nextHair = EditorGUILayout.Popup("Hair color", Mathf.Max(0, currentHair), hairColors);
                if (nextHair != Mathf.Max(0, currentHair))
                    Change(wardrobe, () => appearance.ApplyHairColor(hairColors[nextHair]));
                if (!string.IsNullOrEmpty(appearance.AppearanceKey))
                    EditorGUILayout.LabelField("Character identity", appearance.AppearanceKey);
                EditorGUILayout.Space();
            }
            string[] presets = new[] { "Custom combination" }.Concat(wardrobe.Presets.Select(preset => preset.Id)).ToArray();
            int currentPreset = Array.IndexOf(presets, wardrobe.CurrentOutfitId);
            int nextPreset = EditorGUILayout.Popup("Outfit", Mathf.Max(0, currentPreset), presets);
            if (nextPreset != Mathf.Max(0, currentPreset) && nextPreset > 0)
                Change(wardrobe, () => wardrobe.ApplyOutfit(presets[nextPreset]));

            EditorGUILayout.Space();
            foreach (string slot in wardrobe.Items.Select(item => item.Slot).Distinct())
            {
                string[] options = new[] { "(none)" }.Concat(wardrobe.Items.Where(item => item.Slot == slot).Select(item => item.Id)).ToArray();
                int current = Array.IndexOf(options, wardrobe.GetEquippedItem(slot));
                int next = EditorGUILayout.Popup(ObjectNames.NicifyVariableName(slot), Mathf.Max(0, current), options);
                if (next != Mathf.Max(0, current))
                    Change(wardrobe, () => wardrobe.SetSlot(slot, next == 0 ? null : options[next]));
            }
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("A preset fills the slots. Change individual slots to combine its pieces with other outfits.", MessageType.Info);
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        private void Change(NpcWardrobe wardrobe, Action change)
        {
            UnityEngine.Object[] objects = wardrobe.BodyRenderers.Cast<UnityEngine.Object>()
                .Concat(wardrobe.Garments.Select(part => (UnityEngine.Object)part.Renderer))
                .Append(wardrobe).Append(wardrobe.GetComponent<DefaultNpcAppearance>())
                .Where(obj => obj != null).Distinct().ToArray();
            Undo.RecordObjects(objects, "Change NPC clothing");
            try
            {
                change();
                error = null;
                if (!Application.isPlaying)
                    foreach (UnityEngine.Object obj in objects)
                    {
                        EditorUtility.SetDirty(obj);
                        if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
                    }
            }
            catch (Exception exception) { error = exception.Message; }
        }
    }
}
