using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Editor
{
    /// <summary>Exposes only the projector's controls and puts it last in the
    /// world chain: the print is outside everything else, because the room,
    /// the water and the drink happen to the world, while the print happens to
    /// the picture of it.</summary>
    internal static class BegottenAudioMixerSetup
    {
        private const BindingFlags Flags = BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly string[] Parameters =
        {
            BegottenAudioRules.NativeWeightParameter,
            BegottenAudioRules.NativePausedParameter,
            BegottenAudioRules.NativeResetParameter
        };

        private static readonly string[] Exposed =
        {
            BegottenAudioRules.WeightParameter,
            BegottenAudioRules.PausedParameter,
            BegottenAudioRules.ResetParameter
        };

        public static void Configure(object controller, object group, object projector)
        {
            PlaceLast(group, projector);
            for (int index = 0; index < Parameters.Length; index++)
            {
                Expose(controller, projector, Parameters[index], Exposed[index]);
            }

            PropertyInfo snapshots = RequiredProperty(controller, "snapshots");
            MethodInfo setParameter = RequiredMethod(projector, "SetValueForParameter");
            foreach (object snapshot in (Array)snapshots.GetValue(controller))
            {
                // Every room starts with the projector off. The weight belongs
                // to the transition, never to a scene's own mix.
                foreach (string parameter in Parameters)
                {
                    setParameter.Invoke(projector,
                        new[] { controller, snapshot, parameter, (object)0f });
                }
                EditorUtility.SetDirty((Object)snapshot);
            }

            RequiredProperty(projector, "bypass").SetValue(projector, false);
            RequiredProperty(projector, "enableWetMix").SetValue(projector, false);
            EditorUtility.SetDirty((Object)projector);
            EditorUtility.SetDirty((Object)group);
            EditorUtility.SetDirty((Object)controller);
        }

        private static void PlaceLast(object group, object projector)
        {
            PropertyInfo property = RequiredProperty(group, "effects");
            var effects = new List<object>();
            foreach (object effect in (Array)property.GetValue(group))
                if (!ReferenceEquals(effect, projector)) effects.Add(effect);
            effects.Add(projector);
            Array updated = Array.CreateInstance(property.PropertyType.GetElementType(), effects.Count);
            for (int index = 0; index < effects.Count; index++) updated.SetValue(effects[index], index);
            property.SetValue(group, updated);
        }

        private static void Expose(object controller, object effect, string parameter, string name)
        {
            object guid = RequiredMethod(effect, "GetGUIDForParameter")
                .Invoke(effect, new object[] { parameter });
            if (guid == null || guid.Equals(default(GUID)))
                throw new InvalidOperationException("Missing projector parameter: " + parameter);
            PropertyInfo property = RequiredProperty(controller, "exposedParameters");
            Type entryType = property.PropertyType.GetElementType();
            FieldInfo guidField = entryType.GetField("guid", Flags);
            FieldInfo nameField = entryType.GetField("name", Flags);
            if (guidField == null || nameField == null)
                throw new InvalidOperationException("Unity audio parameter exposure API is unavailable.");
            var entries = new List<object>();
            foreach (object existing in (Array)property.GetValue(controller))
            {
                // Preserve unrelated exposures, and converge duplicate or
                // stale projector entries onto this one.
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
