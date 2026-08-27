using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// The visible column of sun standing in a lancet.
    ///
    /// An OBLIQUE prism, solved once at build time for the baked pose
    /// in <see cref="ChurchInteriorSunRules"/>: its near ring IS the
    /// aperture, welded to the wall, and only the far ring is placed.
    /// A right prism on a rotated transform cannot make that shape -
    /// it would swing the window along with the beam.
    ///
    /// Nothing about it moves at run time. The only thing the clock
    /// changes is whether it is there: shafts by day, none after dark.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChurchLightShaft : MonoBehaviour
    {
        private const string ShaderResourcePath =
            "Shaders/ChurchLightShaft";

        /// <summary>
        /// Above the church grade's 0.62 bloom threshold, so a shaft
        /// haloes the way the lighthouse lens does rather than being a
        /// flat pane of colour.
        /// </summary>
        public const float PeakIntensity = 1.05f;
        public const float ShortestLength = 3.5f;

        public const float LongestLength = 12f;

        /// <summary>
        /// Sunlight is very nearly collimated, but not quite, and a
        /// beam with exactly parallel flanks reads as a solid.
        /// </summary>
        public const float FarSpread = 1.22f;

        private static Material sharedMaterial;

        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock block;
        private Vector3[] vertices;
        private readonly List<Vector3> scratch = new List<Vector3>(8);
        private float wallSide;
        private float halfHeight;
        private float halfWidth;

        public float WallSide => wallSide;
        public bool IsLit => meshRenderer != null && meshRenderer.enabled;
        public float Length { get; private set; }

        public static Material SharedMaterial
        {
            get
            {
                if (sharedMaterial != null)
                {
                    return sharedMaterial;
                }

                Shader shader = Resources.Load<Shader>(
                    ShaderResourcePath);
                if (shader == null)
                {
                    return null;
                }

                sharedMaterial = new Material(shader)
                {
                    name = "Church Light Shaft Shared",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                sharedMaterial.enableInstancing = true;
                return sharedMaterial;
            }
        }

        // Domain reload is off in this project, so a cached material
        // would otherwise outlive the play session that made it.
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedState()
        {
            if (sharedMaterial != null)
            {
                Object.DestroyImmediate(sharedMaterial);
                sharedMaterial = null;
            }
        }

        public static ChurchLightShaft Create(
            Transform parent,
            string name,
            Vector3 localAperture,
            float side,
            float apertureHeight,
            float apertureWidth)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localAperture;
            holder.transform.localRotation = Quaternion.identity;
            ChurchLightShaft shaft =
                holder.AddComponent<ChurchLightShaft>();
            shaft.Initialize(side, apertureHeight, apertureWidth);
            shaft.Bake(ChurchInteriorSunRules.BakedLocalTravel);
            return shaft;
        }

        private void Initialize(
            float side,
            float apertureHeight,
            float apertureWidth)
        {
            wallSide = side;
            halfHeight = apertureHeight * 0.5f;
            halfWidth = apertureWidth * 0.5f;

            vertices = new Vector3[8];
            WriteNearRing();
            for (int index = 0; index < 4; index++)
            {
                vertices[index + 4] = vertices[index];
            }

            mesh = new Mesh
            {
                name = "Church Light Shaft",
                hideFlags = HideFlags.HideAndDontSave,
            };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, 0.25f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.75f),
                new Vector2(1f, 0f), new Vector2(1f, 0.25f),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.75f),
            };
            mesh.triangles = new[]
            {
                0, 1, 5, 0, 5, 4,
                1, 2, 6, 1, 6, 5,
                2, 3, 7, 2, 7, 6,
                3, 0, 4, 3, 4, 7,
                4, 5, 6, 4, 6, 7,
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = SharedMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            meshRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            meshRenderer.allowOcclusionWhenDynamic = false;
            meshRenderer.enabled = false;
            block = new MaterialPropertyBlock();
        }

        private void WriteNearRing()
        {
            // The aperture, in the plane of the wall: the shaft's local
            // X is the wall normal, so the opening lies in Y and Z.
            vertices[0] = new Vector3(0f, -halfHeight, -halfWidth);
            vertices[1] = new Vector3(0f, -halfHeight, halfWidth);
            vertices[2] = new Vector3(0f, halfHeight, halfWidth);
            vertices[3] = new Vector3(0f, halfHeight, -halfWidth);
        }

        /// <summary>
        /// On or off, and how strongly - nothing else. The geometry was
        /// settled when the shaft was built.
        /// </summary>
        public void Apply(Color color, float weight)
        {
            if (mesh == null || meshRenderer == null)
            {
                return;
            }

            if (weight <= 0.001f)
            {
                meshRenderer.enabled = false;
                return;
            }

            meshRenderer.enabled = true;
            meshRenderer.GetPropertyBlock(block);
            block.SetColor("_BeamColor", color);
            block.SetFloat("_Intensity", PeakIntensity * weight);
            meshRenderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// Solves the prism ONCE, at build time, for the baked pose.
        ///
        /// The near ring is the aperture and stays welded to the wall;
        /// only the far ring is placed. It used to be re-solved every
        /// frame against a moving sun, which is why this took a
        /// direction as an argument - it does not any more, and the
        /// mesh is written exactly once in the life of the scene.
        /// </summary>
        private void Bake(Vector3 localTravel)
        {
            Length = ChurchInteriorSunRules.FloorThrow(
                halfHeight + transform.localPosition.y,
                localTravel,
                ShortestLength,
                LongestLength);
            Vector3 reach = localTravel.normalized * Length;
            for (int index = 0; index < 4; index++)
            {
                vertices[index + 4] =
                    (vertices[index] * FarSpread) + reach;
            }

            scratch.Clear();
            scratch.AddRange(vertices);
            mesh.SetVertices(scratch);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (mesh != null)
            {
                Object.DestroyImmediate(mesh);
                mesh = null;
            }
        }
    }
}
