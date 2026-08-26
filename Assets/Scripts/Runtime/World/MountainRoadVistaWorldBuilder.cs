using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public sealed class MountainRoadVistaWorldResult
    {
        internal MountainRoadVistaWorldResult(
            GameObject root,
            Renderer silhouette,
            Renderer lights,
            MountainRoadVistaLightsController controller)
        {
            Root = root;
            Silhouette = silhouette;
            Lights = lights;
            Controller = controller;
        }

        public GameObject Root { get; }
        public Renderer Silhouette { get; }
        public Renderer Lights { get; }
        public MountainRoadVistaLightsController Controller { get; }
    }

    /// <summary>
    /// Two meshes, two shared materials, no colliders and no Lights.
    ///
    /// It is fixed world geometry rather than a camera-relative shell.
    /// The backdrop shell exists because a city lets you walk two hundred
    /// metres and would expose its radius; here the hero can move about
    /// twenty along a parapet, and the parallax of a valley sliding
    /// against the walls of the cut is the effect rather than the
    /// artefact. It also has to be occluded by the parapet and by those
    /// walls, which only real geometry does.
    /// </summary>
    public static class MountainRoadVistaWorldBuilder
    {
        public const string RootName = "Distant Valley";
        public const string SilhouetteName = "Vista Silhouette";
        public const string LightsName = "Vista City Lights";

        private static readonly Color ValleyColor =
            new Color(0.235f, 0.25f, 0.245f);
        private static readonly Color MistColor =
            new Color(0.44f, 0.475f, 0.47f);
        private static readonly Color CityColor =
            new Color(0.215f, 0.225f, 0.235f);
        private static readonly Color HorizonColor =
            new Color(0.29f, 0.315f, 0.33f);

        public static MountainRoadVistaWorldResult Build(
            Transform parent,
            MountainRoadVistaPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);

            Renderer silhouette = BuildLayer(
                root.transform,
                plan,
                SilhouetteName,
                MountainRoadVistaResources.SilhouetteMaterial,
                false);
            Renderer lights = BuildLayer(
                root.transform,
                plan,
                LightsName,
                MountainRoadVistaResources.LightsMaterial,
                true);

            var controller = root.AddComponent<
                MountainRoadVistaLightsController>();
            controller.Initialize(lights);
            return new MountainRoadVistaWorldResult(
                root,
                silhouette,
                lights,
                controller);
        }

        private static Renderer BuildLayer(
            Transform root,
            MountainRoadVistaPlan plan,
            string name,
            Material sharedMaterial,
            bool lightsLayer)
        {
            var vertices = new List<Vector3>(512);
            var colors = new List<Color>(512);
            var uvs = new List<Vector2>(512);
            var triangles = new List<int>(768);
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                MountainRoadVistaPartDescriptor part = plan.Parts[index];
                bool isLight =
                    part.Kind == MountainRoadVistaPartKind.LightPatch;
                if (isLight != lightsLayer)
                {
                    continue;
                }

                AppendBox(
                    part,
                    ResolveColor(part),
                    vertices,
                    colors,
                    uvs,
                    triangles);
            }

            var mesh = new Mesh
            {
                name = name,
                indexFormat = vertices.Count > 65000
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);

            var layer = new GameObject(name);
            layer.transform.SetParent(root, false);
            layer.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = layer.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = sharedMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
            layer.AddComponent<RuntimeGeneratedMeshOwner>()
                .Initialize(mesh);
            return renderer;
        }

        /// <summary>
        /// Vertex colour with the faces separated by a fixed key rather
        /// than by a light. Nothing out here is lit — the material takes
        /// no lighting at all — so the only thing that keeps a box from
        /// reading as a flat card is the value baked into its own faces.
        /// </summary>
        private static void AppendBox(
            MountainRoadVistaPartDescriptor part,
            Color baseColor,
            ICollection<Vector3> vertices,
            ICollection<Color> colors,
            ICollection<Vector2> uvs,
            IList<int> triangles)
        {
            Quaternion rotation = Quaternion.Euler(
                0f,
                part.YawDegrees,
                0f);
            Vector3 half = part.Size * 0.5f;
            Vector3[] faceNormals =
            {
                Vector3.up,
                Vector3.forward,
                Vector3.back,
                Vector3.right,
                Vector3.left,
                Vector3.down
            };
            float[] faceKeys = { 1f, 0.84f, 0.72f, 0.78f, 0.66f, 0.52f };
            for (int face = 0; face < faceNormals.Length; face++)
            {
                Vector3 normal = faceNormals[face];
                Vector3 tangent = face < 1 || face > 4
                    ? Vector3.right
                    : Vector3.Cross(Vector3.up, normal);
                if (tangent.sqrMagnitude < 0.001f)
                {
                    tangent = Vector3.right;
                }

                tangent = tangent.normalized;
                Vector3 bitangent = Vector3.Cross(normal, tangent);
                Vector3 center = Vector3.Scale(normal, half);
                Vector3 u = Vector3.Scale(tangent, half);
                Vector3 v = Vector3.Scale(bitangent, half);
                int start = vertices.Count;
                Color color = baseColor * (faceKeys[face] * part.Shade);
                color.a = 1f;
                Vector3[] corners =
                {
                    center - u - v,
                    center + u - v,
                    center + u + v,
                    center - u + v
                };
                for (int corner = 0; corner < 4; corner++)
                {
                    vertices.Add(
                        part.Center + rotation * corners[corner]);
                    colors.Add(color);
                    uvs.Add(Vector2.zero);
                }

                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 1);
                triangles.Add(start);
                triangles.Add(start + 3);
                triangles.Add(start + 2);
            }
        }

        private static Color ResolveColor(
            MountainRoadVistaPartDescriptor part)
        {
            switch (part.Kind)
            {
                case MountainRoadVistaPartKind.ValleyFloor:
                    return ValleyColor;
                case MountainRoadVistaPartKind.MistBand:
                    return MistColor;
                case MountainRoadVistaPartKind.CityBlock:
                    return CityColor;
                case MountainRoadVistaPartKind.HorizonRidge:
                    return HorizonColor;
                case MountainRoadVistaPartKind.LightPatch:
                    return Color.white;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(part.Kind),
                        part.Kind,
                        null);
            }
        }
    }
}
