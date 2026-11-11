using System;
using System.Collections.Generic;
using UnityEngine;
using Triangle = BarPromenade.GroundSurfacePartitioner.Triangle;
using Recipe = BarPromenade.CityGroundJointField.Recipe;

namespace BarPromenade
{
    internal sealed partial class CityGroundSurfaceSystem
    {
        private static readonly int JointMap = Shader.PropertyToID("_BaseMap");
        private static readonly int JointMapST = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int JointColor = Shader.PropertyToID("_BaseColor");
        private static readonly int JointSmoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int JointKind = Shader.PropertyToID("_GroundKind");
        private static readonly int JointClimate = Shader.PropertyToID("_GroundCityClimate");
        private static readonly int JointClampV = Shader.PropertyToID("_GroundClampV");
        private static readonly int JointRoad = Shader.PropertyToID("_GroundRoadCoordinates");
        private static readonly int JointFrame = Shader.PropertyToID("_CityJointFrame");
        private readonly Dictionary<int, JointProfile> jointProfiles = new Dictionary<int, JointProfile>();
        private CityGroundJointPalette jointPalette;
        private CityGroundJointField staticJointField;
        private Material jointMaterial;
        internal int CoatingJointCount { get; private set; }
        internal int CoatingJointBytes => staticJointField?.Bytes ?? 0;
        internal int MaximumCoatingCandidates => staticJointField?.MaximumCandidates ?? 0;
        internal double CoatingJointBuildMilliseconds { get; private set; }
#if UNITY_EDITOR
        internal static bool BypassJointBuildForMeasurement { get; set; }
#endif

        private void PrepareStaticJoints(List<Triangle> surface, List<Triangle>[] localSurfaces)
        {
#if UNITY_EDITOR
            if (BypassJointBuildForMeasurement) return;
#endif
            if ((grassLayout == null && talusLayout == null) || !CityGroundJointPalette.Supported) return;
            jointPalette = new CityGroundJointPalette();
            var all = new List<Triangle>(surface);
            var neighbourhoods = new List<Rect>();
            // Some Ridge terrains own their collision and are already cut
            // against the streets. Join their appearance without consolidating
            // or replacing those meshes. Home has placed the retained copies.
            foreach (Source source in grassScenery) AppendScenery(source);
            foreach (Source source in jointScenery) AppendScenery(source);
            for (int i = 0; i < localSurfaces.Length; i++)
            {
                DynamicRegion region = dynamicRegions[i];
                Rect neighbourhood = Grow(region.Bounds, 1.1f);
                neighbourhoods.Add(neighbourhood);
                region.JointNeighbours = new List<Triangle>();
                foreach (Triangle triangle in surface)
                    if (neighbourhood.Overlaps(Bounds(triangle))) region.JointNeighbours.Add(triangle);
                all.AddRange(localSurfaces[i]);
            }
            foreach (Source source in sources)
                if (source.Dynamic && source.Renderer.GetComponent<CitySandTreading>() != null)
                    all.AddRange(source.Top);
            if (neighbourhoods.Count > 0)
                all = GroundSurfacePartitioner.Conform(all, bucketSize: EdgeBucketSize,
                    regions: neighbourhoods);
            staticJointField = CreateJointField(all, true);
            if (staticJointField == null)
            {
                jointPalette.Dispose();
                jointPalette = null;
                return;
            }
            foreach (Source source in grassScenery) BindLiveGround(source.Renderer);
            foreach (Source source in jointScenery) BindLiveGround(source.Renderer);
            foreach (Source source in sources)
                if (source.Dynamic && source.Renderer.GetComponent<CitySandTreading>() != null)
                    BindLiveGround(source.Renderer);

            void AppendScenery(Source source)
            {
                if (source.Top == null) Capture(source);
                all.AddRange(source.Top);
                // Coatings cut into passive hillside meshes can end at a
                // T-junction. Subdivide only the samples for the joint field.
                Vector2 min = Vector2.one * float.PositiveInfinity;
                Vector2 max = Vector2.one * float.NegativeInfinity;
                foreach (Triangle triangle in source.Top)
                {
                    if (GetJointProfile(triangle.Surface).Kind != GroundSurfaceKind.Paving) continue;
                    Rect bounds = Bounds(triangle);
                    min = Vector2.Min(min, bounds.min);
                    max = Vector2.Max(max, bounds.max);
                }
                if (max.x > min.x && max.y > min.y)
                    neighbourhoods.Add(Grow(Rect.MinMaxRect(min.x, min.y, max.x, max.y), 1.1f));
            }
        }

        private static Rect Grow(Rect rect, float metres) => Rect.MinMaxRect(
            rect.xMin - metres, rect.yMin - metres, rect.xMax + metres, rect.yMax + metres);

        private CityGroundJointField CreateJointField(IReadOnlyList<Triangle> surface, bool initial)
        {
            var joints = GroundJointTopology.Find(surface, id => GetJointProfile(id).Layer >= 0);
            var edges = new List<CityGroundJointField.Edge>(joints.Count);
            foreach (GroundJointTopology.Joint joint in joints)
            {
                Triangle left = surface[joint.Left], right = surface[joint.Right];
                JointProfile a = GetJointProfile(left.Surface), b = GetJointProfile(right.Surface);
                if (AlreadyContinuous(joint, left, a, right, b) ||
                    !TryJointRecipe(left, a, out Recipe first) || !TryJointRecipe(right, b, out Recipe second) ||
                    SameAppearance(first, second)) continue;
                float width = a.Kind == GroundSurfaceKind.Sand || b.Kind == GroundSurfaceKind.Sand ? 1f :
                    a.Kind == GroundSurfaceKind.Silt || b.Kind == GroundSurfaceKind.Silt ? .19f :
                    IsLoose(a.Kind) || IsLoose(b.Kind) ? .72f : .26f;
                edges.Add(new CityGroundJointField.Edge(joint.A, joint.B, width, first, second,
                    HeightGradient(left), HeightGradient(right)));
            }
            if (edges.Count == 0) return null;
            var field = new CityGroundJointField();
            if (!field.Build(edges))
            {
                GameLog.Warning("city", "ground_joints_budget", GameLog.Field("edges", edges.Count));
                return null;
            }
            if (initial) CoatingJointCount = edges.Count;
            GameLog.Debug("city", "ground_joints_ready", GameLog.Field("edges", edges.Count),
                GameLog.Field("max_cell_candidates", field.MaximumCandidates),
                GameLog.Field("buffer_bytes", field.Bytes), GameLog.Field("static", initial));
            return field;
        }

        private static bool IsLoose(GroundSurfaceKind kind) => kind == GroundSurfaceKind.Soil ||
            kind == GroundSurfaceKind.Lawn || kind == GroundSurfaceKind.Gravel || kind == GroundSurfaceKind.Rock;

        private static Vector2 HeightGradient(Triangle t)
        {
            Vector3 n = Vector3.Cross(t.B.Position - t.A.Position, t.C.Position - t.A.Position);
            return new Vector2(-n.x / n.y, -n.z / n.y);
        }

        private static bool AlreadyContinuous(GroundJointTopology.Joint joint,
            Triangle left, JointProfile a, Triangle right, JointProfile b)
        {
            if (a.Layer != b.Layer || a.ClampV != b.ClampV || a.Dry != b.Dry ||
                a.Wet != b.Wet || a.Physics != b.Physics)
                return false;
            // Compare the UVs on the actual edge, not the derivatives of tiny
            // partition fragments. The latter exaggerate round-off and turn
            // even continuous paving into thousands of unnecessary bands.
            return EqualAt(joint.A) && EqualAt(joint.B);

            bool EqualAt(Vector3 point)
            {
                Vector2 uvA = EdgeUv(left, point, a.ST), uvB = EdgeUv(right, point, b.ST);
                return Mathf.Abs(Mathf.DeltaAngle(uvA.x * 360f, uvB.x * 360f)) <= .18f &&
                    (a.ClampV ? Mathf.Abs(uvA.y - uvB.y) <= .0005f :
                        Mathf.Abs(Mathf.DeltaAngle(uvA.y * 360f, uvB.y * 360f)) <= .18f);
            }
        }

        private static Vector2 EdgeUv(Triangle triangle, Vector3 point, Vector4 st)
        {
            float a = (triangle.A.Position - point).sqrMagnitude;
            float b = (triangle.B.Position - point).sqrMagnitude;
            float c = (triangle.C.Position - point).sqrMagnitude;
            Vector2 uv = a <= b && a <= c ? triangle.A.Uv0 : b <= c ? triangle.B.Uv0 : triangle.C.Uv0;
            return new Vector2(uv.x * st.x + st.z, uv.y * st.y + st.w);
        }

        private JointProfile GetJointProfile(int id)
        {
            if (jointProfiles.TryGetValue(id, out JointProfile profile)) return profile;
            Style style = styles[id];
            MaterialPropertyBlock properties = style.Properties;
            var kind = (GroundSurfaceKind)Mathf.RoundToInt(properties.GetFloat(JointKind));
            profile = new JointProfile(-1, kind, default, default, default, default, false);
            if (style.Material != GroundSurfaceAppearance.SharedMaterial ||
                kind == GroundSurfaceKind.Snow || kind == GroundSurfaceKind.Marking)
                return jointProfiles[id] = profile;
            Texture texture = properties.HasProperty(JointMap) ? properties.GetTexture(JointMap) :
                style.Material.GetTexture(JointMap);
            if (!CitySurfaceLibrary.TryGroundJointMaps(texture, out Texture2D color, out Texture2D response))
                return jointProfiles[id] = profile;
            int layer = jointPalette.Add(color, response);
            if (layer < 0)
            {
                GameLog.Warning("city", "ground_joint_map_unsupported", GameLog.Field("map", color.name));
                return jointProfiles[id] = profile;
            }
            Vector4 st = properties.HasProperty(JointMapST) ? properties.GetVector(JointMapST) :
                new Vector4(1f, 1f, 0f, 0f);
            Color dry = style.HasWeather ? style.DryTint : properties.HasProperty(JointColor) ?
                properties.GetColor(JointColor) : style.Material.GetColor(JointColor);
            Color wet = dry;
            float drySmooth = properties.HasProperty(JointSmoothness) ? properties.GetFloat(JointSmoothness) :
                style.Material.GetFloat(JointSmoothness);
            float wetSmooth = drySmooth;
            if (style.HasWeather)
            {
                CityWetSurfaceSample rain = CityWetSurfaceRules.Evaluate(style.WetKind, 1f);
                drySmooth = CityWetSurfaceRules.Evaluate(style.WetKind, 0f).Smoothness;
                wet = dry * rain.Tint;
                wetSmooth = rain.Smoothness;
            }
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) { dry = dry.linear; wet = wet.linear; }
            float climate = properties.GetFloat(JointClimate);
            float receiver = kind == GroundSurfaceKind.Asphalt && properties.GetFloat(JointRoad) > .5f ? 1f : 0f;
            profile = new JointProfile(layer, kind, st, new Vector4(dry.r, dry.g, dry.b, drySmooth),
                new Vector4(wet.r, wet.g, wet.b, wetSmooth),
                new Vector4(climate, style.HasWeather ? 1f : 0f, receiver, (float)kind),
                properties.GetFloat(JointClampV) > .5f);
            return jointProfiles[id] = profile;
        }

        private static bool TryJointRecipe(Triangle t, JointProfile profile, out Recipe recipe)
        {
            Vector3 a = t.A.Position, b = t.B.Position - a, c = t.C.Position - a;
            float determinant = b.x * c.z - b.z * c.x;
            if (Mathf.Abs(determinant) < 1e-8f) { recipe = default; return false; }
            Vector2 origin = t.A.Uv0, ab = t.B.Uv0 - origin, ac = t.C.Uv0 - origin;
            Vector2 dx = (ab * c.z - ac * b.z) / determinant;
            Vector2 dz = (ac * b.x - ab * c.x) / determinant;
            Vector2 offset = origin - dx * a.x - dz * a.z;
            Vector4 st = profile.ST;
            Vector4 u = new Vector4(Quantize(dx.x * st.x), Quantize(dz.x * st.x),
                Quantize(Mathf.Repeat(offset.x * st.x + st.z, 1f)), profile.Layer);
            float verticalOrigin = offset.y * st.y + st.w;
            Vector4 v = new Vector4(Quantize(dx.y * st.y), Quantize(dz.y * st.y),
                Quantize(profile.ClampV ? verticalOrigin : Mathf.Repeat(verticalOrigin, 1f)),
                profile.ClampV ? 1f : 0f);
            recipe = new Recipe(u, v, profile.Dry, profile.Wet, profile.Physics);
            return true;
        }

        private static float Quantize(float value) => Mathf.Round(value * 10000f) * .0001f;

        private static bool SameAppearance(Recipe a, Recipe b) => a.Equals(b) ||
            (a.U.w == b.U.w && a.V.w == b.V.w && a.Dry == b.Dry && a.Wet == b.Wet && a.Physics == b.Physics &&
            Mathf.Abs(a.U.x - b.U.x) <= .0001f && Mathf.Abs(a.U.y - b.U.y) <= .0001f &&
            Mathf.Abs(a.V.x - b.V.x) <= .0001f && Mathf.Abs(a.V.y - b.V.y) <= .0001f &&
            Mathf.Abs(Mathf.DeltaAngle(a.U.z * 360f, b.U.z * 360f)) <= .1f &&
            (a.V.w > .5f ? Mathf.Abs(a.V.z - b.V.z) <= .0003f :
                Mathf.Abs(Mathf.DeltaAngle(a.V.z * 360f, b.V.z * 360f)) <= .1f));

        private Material JointMaterial => jointMaterial ??= CreateJointMaterial();

        private static Material CreateJointMaterial()
        {
            var result = new Material(GroundSurfaceAppearance.SharedMaterial)
            {
                name = "City ground joints (shared)", hideFlags = HideFlags.HideAndDontSave
            };
            result.EnableKeyword("_CITY_GROUND_SEAMS");
            return result;
        }

        private void BindJointSlot(Renderer renderer, int slot, CityGroundJointField field, MaterialPropertyBlock block)
        {
            GroundSurfaceAppearance.ReadProperties(renderer, block, slot);
            field.Bind(block, jointPalette);
            block.SetMatrix(JointFrame, transform.worldToLocalMatrix);
            GroundSurfaceAppearance.WriteProperties(renderer, block, slot);
        }

        private void BindLiveGround(Renderer renderer)
        {
            Material[] materials = renderer.sharedMaterials;
            var block = new MaterialPropertyBlock();
            for (int slot = 0; slot < materials.Length; slot++)
            {
                if (materials[slot] != GroundSurfaceAppearance.SharedMaterial) continue;
                renderer.GetPropertyBlock(block, slot);
                int index = materials.Length == 1 && block.isEmpty ? -1 : slot;
                BindJointSlot(renderer, index, staticJointField, block);
                materials[slot] = JointMaterial;
            }
            renderer.sharedMaterials = materials;
        }

        private void OnDestroy()
        {
            foreach (DynamicRegion region in dynamicRegions) region.JointField?.Dispose();
            staticJointField?.Dispose();
            jointPalette?.Dispose();
            CityGroundJointPalette.Release(jointMaterial);
        }

        private readonly struct JointProfile
        {
            internal JointProfile(int layer, GroundSurfaceKind kind, Vector4 st, Vector4 dry,
                Vector4 wet, Vector4 physics, bool clampV)
            { Layer = layer; Kind = kind; ST = st; Dry = dry; Wet = wet; Physics = physics; ClampV = clampV; }
            internal readonly int Layer;
            internal readonly GroundSurfaceKind Kind;
            internal readonly Vector4 ST, Dry, Wet, Physics;
            internal readonly bool ClampV;
        }
    }
}
