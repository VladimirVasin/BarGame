using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BarPromenade
{
    [Serializable]
    public sealed class HomeAuthoredPart
    {
        public string name;
        public string semantic_name;
        public string[] aliases;
        public string[] patterns;
        public string role;
        public string group;
        public string sheet;
        public string fit;
        public string primitive_kind;
        public float[] tint;
        public float[] position;
        public float[] rotation;
        public float[] size;
        public float[] bounds_min;
        public float[] bounds_max;
        public int min_day = 1;
        public int max_day = 7;
        public int triangles;
        public int grid_columns;
        public int grid_rows;
        public float[] grid_top_heights;
        public float[] grid_bottom_heights;
        // Import-owned mapping: each leading upper-shell vertex addresses
        // one authored height sample, including duplicate facet corners.
        public int[] grid_vertex_samples;
        public float max_depth;
        public bool collider;
        public Mesh mesh;

        public bool IsParametric => fit == "parametric";
        public Vector3 Size => Vector(size);
        public Vector3 Position => Vector(position);
        public Vector3 Rotation => Vector(rotation);
        public Color Tint => tint != null && tint.Length >= 3
            ? new Color(tint[0], tint[1], tint[2], tint.Length > 3 ? tint[3] : 1f)
            : Color.white;

        public static Vector3 Vector(float[] values) =>
            values != null && values.Length >= 3
                ? new Vector3(values[0], values[1], values[2])
                : Vector3.zero;
    }

    [Serializable]
    public sealed class HomeAuthoredManifest
    {
        public int schema_version;
        public string design_id;
        public string generator_version;
        public string signature;
        public HomeAuthoredPart[] parts;
    }

    /// <summary>Passive Blender meshes, normalized to Unity metres at import.
    /// The manifest owns their shape; the layout still owns placement and collision.</summary>
    public sealed class HomeInteriorModelLibrary : ScriptableObject
    {
        public const string ResourcePath = "Home/HomeInteriorModels";
        [SerializeField] private HomeAuthoredPart[] parts = Array.Empty<HomeAuthoredPart>();
        [SerializeField] private string buildSignature;
        private Dictionary<string, HomeAuthoredPart> bindings;
        private readonly List<(Regex pattern, HomeAuthoredPart part)> patterns =
            new List<(Regex, HomeAuthoredPart)>();
        private static HomeInteriorModelLibrary cached;

        public IReadOnlyList<HomeAuthoredPart> Parts => parts;
        public string BuildSignature => buildSignature;
        public static HomeInteriorModelLibrary Load()
        {
            if (cached == null)
                cached = Resources.Load<HomeInteriorModelLibrary>(ResourcePath);
            if (cached == null)
                throw new InvalidOperationException("Missing authored Home model library. Run Home model asset setup.");
            return cached;
        }

        public void Configure(HomeAuthoredPart[] importedParts, string signature)
        {
            parts = importedParts ?? throw new ArgumentNullException(nameof(importedParts));
            buildSignature = signature;
            bindings = null;
        }

        public HomeAuthoredPart Binding(string semanticName, string primitiveKind = null)
        {
            if (bindings == null)
            {
                bindings = new Dictionary<string, HomeAuthoredPart>(StringComparer.Ordinal);
                patterns.Clear();
                foreach (HomeAuthoredPart part in parts)
                {
                    if (part.role == "decor") continue;
                    string prefix = (part.primitive_kind ?? string.Empty) + "\0";
                    if (!string.IsNullOrEmpty(part.semantic_name)) bindings[prefix + part.semantic_name] = part;
                    foreach (string alias in part.aliases ?? Array.Empty<string>())
                        if (!bindings.ContainsKey(prefix + alias)) bindings.Add(prefix + alias, part);
                    foreach (string pattern in part.patterns ?? Array.Empty<string>())
                        patterns.Add((new Regex(pattern, RegexOptions.CultureInvariant), part));
                }
            }
            if (bindings.TryGetValue("\0" + semanticName, out HomeAuthoredPart found)) return found;
            if (primitiveKind != null && bindings.TryGetValue(primitiveKind + "\0" + semanticName, out found)) return found;
            foreach (var candidate in patterns)
                if ((primitiveKind == null || string.IsNullOrEmpty(candidate.part.primitive_kind) ||
                     candidate.part.primitive_kind == primitiveKind) &&
                    candidate.pattern.IsMatch(semanticName)) return candidate.part;
            throw new InvalidOperationException($"Home model has no authored binding for '{semanticName}'.");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => cached = null;
    }
}
