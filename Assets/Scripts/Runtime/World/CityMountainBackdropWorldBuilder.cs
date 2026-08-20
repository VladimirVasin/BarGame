using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// The presentation-only ridge shell. It has no collider, Light, nav
    /// contribution or world bounds contract; callers deliberately keep it
    /// outside the physical City's result bounds.
    /// </summary>
    public sealed class CityMountainBackdropWorldResult
    {
        internal CityMountainBackdropWorldResult(
            GameObject root,
            CityMountainBackdropFollower follower,
            IReadOnlyList<MeshRenderer> ridgeRenderers)
        {
            Root = root;
            Follower = follower;
            RidgeRenderers = ridgeRenderers;
        }

        public GameObject Root { get; }
        public CityMountainBackdropFollower Follower { get; }
        public IReadOnlyList<MeshRenderer> RidgeRenderers { get; }
    }

    /// <summary>
    /// Builds two low-poly depth layers around only the world-west and
    /// world-south azimuths. Their 39-44 m radii stay inside City's fixed
    /// 48 m far plane and leave deliberate open sky to the north and east.
    /// </summary>
    public static class CityMountainBackdropWorldBuilder
    {
        public const float NearLayerRadius = 39.4f;
        public const float FarLayerRadius = 43.2f;

        private const float WestStartDegrees = 135f;
        private const float WestEndDegrees = 225f;
        private const float SouthStartDegrees = WestEndDegrees;
        private const float SouthEndDegrees = 315f;
        private const int SegmentsPerSector = 12;

        private static readonly RidgeLayer FarLayer = new RidgeLayer(
            "Far",
            FarLayerRadius,
            0.55f,
            -8.4f,
            12.0f,
            19.0f,
            new Color(0.170f, 0.220f, 0.205f, 0.72f),
            new Color(0.245f, 0.300f, 0.275f, 0.72f),
            0x4D42,
            -20);

        private static readonly RidgeLayer NearLayer = new RidgeLayer(
            "Near",
            NearLayerRadius,
            0.90f,
            -8.8f,
            7.0f,
            13.5f,
            new Color(0.105f, 0.145f, 0.132f, 0.88f),
            new Color(0.180f, 0.225f, 0.205f, 0.88f),
            0x4E52,
            -10);

        public static CityMountainBackdropWorldResult Build(
            Transform parent,
            Camera trackingCamera = null)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            var rootObject = new GameObject("Mountain Backdrop");
            rootObject.transform.SetParent(parent, false);
            Material sharedMaterial =
                CityMountainBackdropResources.SharedMaterial;
            var renderers = new List<MeshRenderer>(4);

            BuildLayer(rootObject.transform, FarLayer, sharedMaterial, renderers);
            BuildLayer(rootObject.transform, NearLayer, sharedMaterial, renderers);

            CityMountainBackdropFollower follower =
                rootObject.AddComponent<CityMountainBackdropFollower>();
            follower.Initialize(trackingCamera);
            return new CityMountainBackdropWorldResult(
                rootObject,
                follower,
                renderers.AsReadOnly());
        }

        private static void BuildLayer(
            Transform parent,
            RidgeLayer layer,
            Material sharedMaterial,
            ICollection<MeshRenderer> renderers)
        {
            renderers.Add(BuildSector(
                parent,
                "West",
                WestStartDegrees,
                WestEndDegrees,
                true,
                false,
                false,
                layer,
                sharedMaterial));
            renderers.Add(BuildSector(
                parent,
                "South",
                SouthStartDegrees,
                SouthEndDegrees,
                false,
                true,
                true,
                layer,
                sharedMaterial));
        }

        private static MeshRenderer BuildSector(
            Transform parent,
            string sectorName,
            float startDegrees,
            float endDegrees,
            bool taperStart,
            bool taperEnd,
            bool carveRiverNotch,
            RidgeLayer layer,
            Material sharedMaterial)
        {
            string name =
                $"Mountain Backdrop {sectorName} {layer.Name}";
            Mesh mesh = CreateRidgeMesh(
                name,
                startDegrees,
                endDegrees,
                taperStart,
                taperEnd,
                carveRiverNotch,
                layer);
            var ridge = new GameObject(name);
            ridge.transform.SetParent(parent, false);
            ridge.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = ridge.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = sharedMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.sortingOrder = layer.SortingOrder;
            ridge.AddComponent<RuntimeGeneratedMeshOwner>()
                .Initialize(mesh);
            // This mesh is tiny and intentionally remains readable so the
            // river-axis notch can be verified as presentation data.
            mesh.UploadMeshData(false);
            return renderer;
        }

        private static Mesh CreateRidgeMesh(
            string name,
            float startDegrees,
            float endDegrees,
            bool taperStart,
            bool taperEnd,
            bool carveRiverNotch,
            RidgeLayer layer)
        {
            int triangleCount = SegmentsPerSector * 2;
            var vertices = new List<Vector3>(triangleCount * 3);
            var colors = new List<Color>(triangleCount * 3);
            var uvs = new List<Vector2>(triangleCount * 3);
            var triangles = new List<int>(triangleCount * 3);

            for (int segment = 0;
                 segment < SegmentsPerSector;
                 segment++)
            {
                float firstT = segment / (float)SegmentsPerSector;
                float secondT = (segment + 1f) / SegmentsPerSector;
                float firstAngle = Mathf.Lerp(
                    startDegrees,
                    endDegrees,
                    firstT);
                float secondAngle = Mathf.Lerp(
                    startDegrees,
                    endDegrees,
                    secondT);

                float firstBottomHeight =
                    SampleBottom(firstAngle, layer);
                float secondBottomHeight =
                    SampleBottom(secondAngle, layer);
                float firstPeakHeight = ApplyEdgeTaper(
                    SamplePeak(firstAngle, layer),
                    firstBottomHeight,
                    firstT,
                    taperStart,
                    taperEnd);
                float secondPeakHeight = ApplyEdgeTaper(
                    SamplePeak(secondAngle, layer),
                    secondBottomHeight,
                    secondT,
                    taperStart,
                    taperEnd);
                firstPeakHeight = ApplyRiverNotch(
                    firstPeakHeight,
                    firstBottomHeight,
                    firstAngle,
                    carveRiverNotch);
                secondPeakHeight = ApplyRiverNotch(
                    secondPeakHeight,
                    secondBottomHeight,
                    secondAngle,
                    carveRiverNotch);

                Vector3 firstBottom = CreatePoint(
                    firstAngle,
                    SampleRadius(firstAngle, layer),
                    firstBottomHeight);
                Vector3 firstPeak = CreatePoint(
                    firstAngle,
                    SampleRadius(firstAngle, layer),
                    firstPeakHeight);
                Vector3 secondBottom = CreatePoint(
                    secondAngle,
                    SampleRadius(secondAngle, layer),
                    secondBottomHeight);
                Vector3 secondPeak = CreatePoint(
                    secondAngle,
                    SampleRadius(secondAngle, layer),
                    secondPeakHeight);

                float faceShade = Mathf.Lerp(
                    0.82f,
                    1.04f,
                    Hash01(
                        Mathf.RoundToInt(
                            (firstAngle + secondAngle) * 100f),
                        layer.Seed + 71));
                AppendTriangle(
                    firstBottom,
                    firstPeak,
                    secondPeak,
                    layer,
                    faceShade,
                    vertices,
                    colors,
                    uvs,
                    triangles);
                AppendTriangle(
                    firstBottom,
                    secondPeak,
                    secondBottom,
                    layer,
                    faceShade * 0.94f,
                    vertices,
                    colors,
                    uvs,
                    triangles);
            }

            var mesh = new Mesh
            {
                name = $"{name} Mesh",
                hideFlags = HideFlags.HideAndDontSave
            };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float ApplyEdgeTaper(
            float peakHeight,
            float bottomHeight,
            float sectorT,
            bool taperStart,
            bool taperEnd)
        {
            float envelope = 1f;
            if (taperStart)
            {
                envelope *= Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(sectorT / 0.18f));
            }

            if (taperEnd)
            {
                envelope *= Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01((1f - sectorT) / 0.18f));
            }

            return Mathf.Lerp(bottomHeight, peakHeight, envelope);
        }

        private static float ApplyRiverNotch(
            float peakHeight,
            float bottomHeight,
            float angleDegrees,
            bool carveRiverNotch)
        {
            if (!carveRiverNotch)
            {
                return peakHeight;
            }

            float distanceFromSouth = Mathf.Abs(
                Mathf.DeltaAngle(270f, angleDegrees));
            float envelope = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(10f, 22f, distanceFromSouth));
            return Mathf.Lerp(bottomHeight, peakHeight, envelope);
        }

        private static void AppendTriangle(
            Vector3 first,
            Vector3 second,
            Vector3 third,
            RidgeLayer layer,
            float shade,
            ICollection<Vector3> vertices,
            ICollection<Color> colors,
            ICollection<Vector2> uvs,
            ICollection<int> triangles)
        {
            int firstIndex = vertices.Count;
            vertices.Add(first);
            vertices.Add(second);
            vertices.Add(third);
            colors.Add(Shade(layer.LowerColor, shade));
            colors.Add(Shade(layer.UpperColor, shade));
            colors.Add(Shade(
                third.y > 0f ? layer.UpperColor : layer.LowerColor,
                shade));
            uvs.Add(Vector2.zero);
            uvs.Add(Vector2.up);
            uvs.Add(new Vector2(1f, third.y > 0f ? 1f : 0f));
            triangles.Add(firstIndex);
            triangles.Add(firstIndex + 1);
            triangles.Add(firstIndex + 2);
        }

        private static Vector3 CreatePoint(
            float angleDegrees,
            float radius,
            float height)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            return new Vector3(
                Mathf.Cos(radians) * radius,
                height,
                Mathf.Sin(radians) * radius);
        }

        private static float SampleRadius(
            float angleDegrees,
            RidgeLayer layer)
        {
            float noise = Hash01(
                Mathf.RoundToInt(angleDegrees * 100f),
                layer.Seed + 11) * 2f - 1f;
            return layer.Radius + noise * layer.RadialVariation;
        }

        private static float SampleBottom(
            float angleDegrees,
            RidgeLayer layer)
        {
            return layer.BottomHeight -
                   Hash01(
                       Mathf.RoundToInt(angleDegrees * 100f),
                       layer.Seed + 23) * 0.9f;
        }

        private static float SamplePeak(
            float angleDegrees,
            RidgeLayer layer)
        {
            float random = Hash01(
                Mathf.RoundToInt(angleDegrees * 100f),
                layer.Seed + 37);
            float broadShape =
                Mathf.Sin(
                    angleDegrees * Mathf.Deg2Rad * 2.7f +
                    layer.Seed * 0.001f) * 0.75f;
            return Mathf.Clamp(
                Mathf.Lerp(
                    layer.MinimumPeak,
                    layer.MaximumPeak,
                    random) + broadShape,
                layer.MinimumPeak,
                layer.MaximumPeak);
        }

        private static Color Shade(Color source, float multiplier)
        {
            return new Color(
                source.r * multiplier,
                source.g * multiplier,
                source.b * multiplier,
                source.a);
        }

        private static float Hash01(int value, int seed)
        {
            uint hash = unchecked((uint)(value ^ seed));
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return (hash & 0x00FFFFFFu) / 16777215f;
        }

        private readonly struct RidgeLayer
        {
            public RidgeLayer(
                string name,
                float radius,
                float radialVariation,
                float bottomHeight,
                float minimumPeak,
                float maximumPeak,
                Color lowerColor,
                Color upperColor,
                int seed,
                int sortingOrder)
            {
                Name = name;
                Radius = radius;
                RadialVariation = radialVariation;
                BottomHeight = bottomHeight;
                MinimumPeak = minimumPeak;
                MaximumPeak = maximumPeak;
                LowerColor = lowerColor;
                UpperColor = upperColor;
                Seed = seed;
                SortingOrder = sortingOrder;
            }

            public string Name { get; }
            public float Radius { get; }
            public float RadialVariation { get; }
            public float BottomHeight { get; }
            public float MinimumPeak { get; }
            public float MaximumPeak { get; }
            public Color LowerColor { get; }
            public Color UpperColor { get; }
            public int Seed { get; }
            public int SortingOrder { get; }
        }
    }
}
