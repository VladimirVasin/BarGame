using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Editor
{
    /// <summary>Exposes only the tape controls; room snapshots keep their own mix.</summary>
    internal static class IntoxicationAudioMixerSetup
    {
        private const BindingFlags Flags = BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.Instance;

        public static void Configure(object controller, object effect)
        {
            Expose(controller, effect, "Intensity", IntoxicationAudioDriver.IntensityParameter);
            Expose(controller, effect, "Paused", IntoxicationAudioDriver.PausedParameter);
            Expose(controller, effect, "Reset", IntoxicationAudioDriver.ResetParameter);

            PropertyInfo snapshots = controller.GetType().GetProperty("snapshots", Flags);
            MethodInfo setParameter = effect.GetType().GetMethod("SetValueForParameter", Flags);
            foreach (object snapshot in (Array)snapshots.GetValue(controller))
            {
                foreach (string name in new[] { "Intensity", "Paused", "Reset" })
                {
                    setParameter.Invoke(effect, new[] { controller, snapshot, name, (object)0f });
                }
                EditorUtility.SetDirty((Object)snapshot);
            }

            EditorUtility.SetDirty((Object)effect);
            EditorUtility.SetDirty((Object)controller);
        }

        private static void Expose(object controller, object effect, string parameter, string name)
        {
            Type controllerType = controller.GetType();
            PropertyInfo property = controllerType.GetProperty("exposedParameters", Flags);
            MethodInfo getGuid = effect.GetType().GetMethod("GetGUIDForParameter", Flags);
            if (property == null || getGuid == null)
            {
                throw new InvalidOperationException("Unity audio parameter exposure API is unavailable.");
            }

            object guid = getGuid.Invoke(effect, new object[] { parameter });
            if (guid.Equals(default(GUID)))
            {
                throw new InvalidOperationException("Missing VHS parameter: " + parameter);
            }

            Type entryType = property.PropertyType.GetElementType();
            FieldInfo guidField = entryType.GetField("guid", Flags);
            FieldInfo nameField = entryType.GetField("name", Flags);
            var entries = new List<object>();
            foreach (object existing in (Array)property.GetValue(controller))
            {
                // Preserve unrelated exposures, and converge duplicate/stale tape entries.
                if (!guid.Equals(guidField.GetValue(existing)) &&
                    !string.Equals(name, (string)nameField.GetValue(existing), StringComparison.Ordinal))
                {
                    entries.Add(existing);
                }
            }

            object entry = Activator.CreateInstance(entryType);
            guidField.SetValue(entry, guid);
            nameField.SetValue(entry, name);
            entries.Add(entry);
            Array updated = Array.CreateInstance(entryType, entries.Count);
            for (int index = 0; index < entries.Count; index++)
            {
                updated.SetValue(entries[index], index);
            }
            property.SetValue(controller, updated);
            controllerType.GetMethod("OnChangedExposedParameter", Flags)?.Invoke(controller, null);
        }
    }
}
