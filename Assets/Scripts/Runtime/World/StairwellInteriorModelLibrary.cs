using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [Serializable]
    public sealed class StairwellAuthoredPart
    {
        public string name;
        public string[] aliases;
        public string kind;
        public string style;
        public float[] size;
        public float[] position;
        public float[] bounds_min;
        public float[] bounds_max;
        public int triangles;
        public Mesh mesh;
        public Vector3 Size => Vector(size);
        public static Vector3 Vector(float[] value) => value != null && value.Length == 3
            ? new Vector3(value[0], value[1], value[2]) : Vector3.zero;
    }

    [Serializable]
    public sealed class StairwellAuthoredManifest
    {
        public int schema_version;
        public string design_id;
        public string generator_version;
        public string signature;
        public StairwellAuthoredPart[] parts;
    }

    /// <summary>Fixed-metre Blender parts; import normalizes meshes to the legacy
    /// unit-transform contract so colliders, foot probes and all pivots stay exact.</summary>
    public sealed class StairwellInteriorModelLibrary : ScriptableObject
    {
        public const string ResourcePath = "Stairwell/StairwellInteriorModels";
        [SerializeField] private StairwellAuthoredPart[] parts = Array.Empty<StairwellAuthoredPart>();
        [SerializeField] private string buildSignature;
        private Dictionary<string, List<StairwellAuthoredPart>> bindings;
        private static StairwellInteriorModelLibrary cached;
        public IReadOnlyList<StairwellAuthoredPart> Parts => parts;
        public string BuildSignature => buildSignature;

        public static StairwellInteriorModelLibrary Load()
        {
            if (cached == null) cached = Resources.Load<StairwellInteriorModelLibrary>(ResourcePath);
            if (cached == null) throw new InvalidOperationException("Missing authored Stairwell model library. Run Stairwell model setup.");
            return cached;
        }

        public void Configure(StairwellAuthoredPart[] imported, string signature)
        {
            if (imported == null || imported.Length == 0 || imported.Length > 128)
                throw new ArgumentException("Invalid bounded Stairwell mesh library.", nameof(imported));
            parts = imported;
            buildSignature = signature;
            bindings = null;
        }

        private void EnsureBindings()
        {
            if (bindings == null)
            {
                bindings = new Dictionary<string, List<StairwellAuthoredPart>>(StringComparer.Ordinal);
                foreach (StairwellAuthoredPart part in parts)
                    foreach (string alias in part.aliases)
                    {
                        string key = part.kind + "\0" + alias;
                        if (!bindings.TryGetValue(key, out List<StairwellAuthoredPart> variants))
                            bindings.Add(key, variants = new List<StairwellAuthoredPart>(2));
                        variants.Add(part);
                    }
            }
        }

        public StairwellAuthoredPart UniqueBinding(string semanticName, string kind)
        {
            EnsureBindings();
            if (bindings.TryGetValue(kind + "\0" + semanticName, out List<StairwellAuthoredPart> matches) && matches.Count == 1)
                return matches[0];
            throw new InvalidOperationException("Missing unique fixed Stairwell binding " + semanticName);
        }

        public StairwellAuthoredPart Binding(string semanticName, Vector3 size, string kind)
        {
            EnsureBindings();
            if (bindings.TryGetValue(kind + "\0" + semanticName, out List<StairwellAuthoredPart> matches))
                foreach (StairwellAuthoredPart part in matches)
                    if ((part.Size - size).sqrMagnitude < .000001f && part.mesh != null) return part;
            throw new InvalidOperationException($"Stairwell has no fixed authored {kind} for '{semanticName}' at {size}.");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => cached = null;
    }
}
