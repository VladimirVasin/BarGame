using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Editor
{
    /// <summary>One temporary underwater mix after the existing world perception effect.</summary>
    internal static class UnderwaterAudioMixerSetup
    {
        private const BindingFlags Flags = BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.Instance;
        private const string CutoffParameter = "Cutoff freq";
        private const string ExposedCutoff = "BowlWaterCutoffHz";
        private const string ExposedGain = "BowlWaterGainDb";

        public static void Configure(object controller, object group, object tape, object lowpass)
        {
            PlaceAfterTape(group, tape, lowpass);
            // Unity owns exactly one Attenuation per group. Perception has no
            // ordinary gain owner and remains 0 dB in every room snapshot.
            Expose(controller, RequiredMethod(lowpass, "GetGUIDForParameter")
                .Invoke(lowpass, new object[] { CutoffParameter }), ExposedCutoff);
            Expose(controller, RequiredMethod(group, "GetGUIDForVolume")
                .Invoke(group, null), ExposedGain);

            PropertyInfo snapshots = RequiredProperty(controller, "snapshots");
            MethodInfo setParameter = RequiredMethod(lowpass, "SetValueForParameter");
            MethodInfo setVolume = RequiredMethod(group, "SetValueForVolume");
            foreach (object snapshot in (Array)snapshots.GetValue(controller))
            {
                setParameter.Invoke(lowpass,
                    new[] { controller, snapshot, CutoffParameter, (object)22000f });
                setParameter.Invoke(lowpass,
                    new[] { controller, snapshot, "Resonance", (object)1f });
                setVolume.Invoke(group, new[] { controller, snapshot, (object)0f });
                EditorUtility.SetDirty((Object)snapshot);
            }

            RequiredProperty(lowpass, "bypass").SetValue(lowpass, false);
            RequiredProperty(lowpass, "enableWetMix").SetValue(lowpass, false);
            EditorUtility.SetDirty((Object)lowpass);
            EditorUtility.SetDirty((Object)group);
            EditorUtility.SetDirty((Object)controller);
        }

        private static void PlaceAfterTape(object group, object tape, object lowpass)
        {
            PropertyInfo property = RequiredProperty(group, "effects");
            var effects = new List<object>();
            foreach (object effect in (Array)property.GetValue(group))
                if (!ReferenceEquals(effect, lowpass)) effects.Add(effect);
            int tapeIndex = effects.FindIndex(effect => ReferenceEquals(effect, tape));
            if (tapeIndex < 0)
                throw new InvalidOperationException("World perception group has no VHS effect.");
            effects.Insert(tapeIndex + 1, lowpass);
            Array updated = Array.CreateInstance(property.PropertyType.GetElementType(), effects.Count);
            for (int index = 0; index < effects.Count; index++) updated.SetValue(effects[index], index);
            property.SetValue(group, updated);
        }

        private static void Expose(object controller, object guid, string name)
        {
            if (guid == null || guid.Equals(default(GUID)))
                throw new InvalidOperationException("Missing underwater mixer parameter: " + name);
            PropertyInfo property = RequiredProperty(controller, "exposedParameters");
            Type entryType = property.PropertyType.GetElementType();
            FieldInfo guidField = entryType.GetField("guid", Flags);
            FieldInfo nameField = entryType.GetField("name", Flags);
            if (guidField == null || nameField == null)
                throw new InvalidOperationException("Unity audio parameter exposure API is unavailable.");
            var entries = new List<object>();
            foreach (object existing in (Array)property.GetValue(controller))
            {
                if (!guid.Equals(guidField.GetValue(existing)) &&
                    !string.Equals(name, (string)nameField.GetValue(existing), StringComparison.Ordinal))
                    entries.Add(existing);
            }

            object entry = Activator.CreateInstance(entryType);
            guidField.SetValue(entry, guid);
            nameField.SetValue(entry, name);
            entries.Add(entry);
            Array updated = Array.CreateInstance(entryType, entries.Count);
            for (int index = 0; index < entries.Count; index++) updated.SetValue(entries[index], index);
            property.SetValue(controller, updated);
            RequiredMethod(controller, "OnChangedExposedParameter").Invoke(controller, null);
        }

        private static MethodInfo RequiredMethod(object target, string name) =>
            target.GetType().GetMethod(name, Flags) ??
            throw new MissingMethodException(target.GetType().FullName, name);

        private static PropertyInfo RequiredProperty(object target, string name) =>
            target.GetType().GetProperty(name, Flags) ??
            throw new MissingMemberException(target.GetType().FullName, name);
    }
}
