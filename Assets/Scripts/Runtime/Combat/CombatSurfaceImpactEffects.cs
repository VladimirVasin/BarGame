using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Scene-owned bullet chips and persistent surface marks, driven by the live duel clock.</summary>
    public sealed class CombatSurfaceImpactEffects : MonoBehaviour
    {
        public const int MaximumHoles = 128, MaximumParticles = 192;
        private sealed class Hole
        {
            internal Transform Transform;
            internal Collider Surface;
            internal Vector3 LocalPoint, LocalNormal;
            internal float Roll;
        }

        private static Material sharedHoleMaterial;
        private readonly Hole[] holes = new Hole[MaximumHoles];
        private ParticleSystem particles;
        private Mesh holeMesh;
        private Vector3 meshNormal;
        private float meshUnit;
        private int cursor;
        private uint random = 0x738a21u;
        public int HoleCount { get; private set; }
        public int EmissionCount { get; private set; }
        public int ActiveParticleCount => particles != null ? particles.particleCount : 0;
        internal float SimulationSeconds { get; private set; }

        internal void Initialize()
        {
            GameObject shapes = Resources.Load<GameObject>("CombatBlood/BloodShapes");
            if (shapes != null)
                foreach (MeshFilter shape in shapes.GetComponentsInChildren<MeshFilter>(true))
                    if (shape.name == "Splat0") { holeMesh = shape.sharedMesh; break; }
            if (holeMesh == null) throw new InvalidOperationException("Bullet surface marks require the authored splat plane.");
            Vector3 size = holeMesh.bounds.size;
            meshUnit = Mathf.Max(size.x, size.y, size.z);
            Vector3[] vertices = holeMesh.vertices;
            int[] triangles = holeMesh.triangles;
            meshNormal = Vector3.Cross((vertices[triangles[1]] - vertices[triangles[0]]) / meshUnit,
                (vertices[triangles[2]] - vertices[triangles[0]]) / meshUnit).normalized;
            if (meshUnit <= 0f || meshNormal.sqrMagnitude < .9f)
                throw new InvalidOperationException("Bullet surface marks require a non-degenerate authored plane.");
            if (sharedHoleMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/CombatSurfaceImpact");
                if (shader == null) throw new InvalidOperationException("Missing bullet surface impact shader.");
                sharedHoleMaterial = new Material(shader) { name = "Shared bullet surface marks" };
            }
            var host = new GameObject("Bullet surface chips");
            host.transform.SetParent(transform, false);
            particles = host.AddComponent<ParticleSystem>();
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.useAutoRandomSeed = false; particles.randomSeed = 0x738a21u;
            var main = particles.main;
            main.playOnAwake = false; main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaximumParticles; main.gravityModifier = 1f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = particles.emission; emission.enabled = false;
            var shapeModule = particles.shape; shapeModule.enabled = false;
            var color = particles.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(.5f, .46f, .4f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = CityNightResources.AtmosphereMaterial;
            // Atmosphere's metre-wide depth fade would erase chips only a few
            // centimetres off a wall. Override this renderer, not the shared fog.
            var properties = new MaterialPropertyBlock();
            properties.SetFloat("_SoftParticleDistance", .015f);
            properties.SetFloat("_EdgePower", 1f);
            renderer.SetPropertyBlock(properties);
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = .025f; renderer.lengthScale = 1.2f;
            renderer.maxParticleSize = .035f;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            particles.Pause(false);
        }

        internal void Emit(Collider surface, Vector3 point, Vector3 normal, Vector3 incoming)
        {
            if (surface == null || particles == null) return;
            normal = normal.sqrMagnitude > .5f ? normal.normalized : -incoming.normalized;
            if (normal.sqrMagnitude < .5f) normal = Vector3.up;
            Hole hole = holes[cursor];
            if (hole == null)
            {
                var host = new GameObject("Bullet surface hole");
                host.transform.SetParent(transform, false);
                host.AddComponent<MeshFilter>().sharedMesh = holeMesh;
                MeshRenderer renderer = host.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = sharedHoleMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                hole = holes[cursor] = new Hole { Transform = host.transform };
            }
            if (!hole.Transform.gameObject.activeSelf || hole.Surface == null) HoleCount++;
            hole.Surface = surface;
            hole.LocalPoint = surface.transform.InverseTransformPoint(point);
            // Normals use inverse transpose, including non-uniform surface scale.
            hole.LocalNormal = surface.transform.localToWorldMatrix.transpose.MultiplyVector(normal).normalized;
            hole.Roll = Range(0f, 360f);
            hole.Transform.localScale = Vector3.one * (.11f / meshUnit);
            hole.Transform.gameObject.SetActive(true);
            Refresh(hole);
            cursor = (cursor + 1) % holes.Length;
            EmissionCount++;
            Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) > .9f ? Vector3.forward : Vector3.up).normalized;
            Vector3 bitangent = Vector3.Cross(normal, tangent);
            for (int i = 0; i < 16; i++)
                particles.Emit(new ParticleSystem.EmitParams
                {
                    position = point + normal * .008f,
                    velocity = normal * Range(.7f, 1.8f) + tangent * Range(-1.2f, 1.2f) + bitangent * Range(-1.2f, 1.2f),
                    startLifetime = Range(.16f, .34f), startSize = Range(.012f, .032f),
                    startColor = i < 3 ? new Color(1.7f, 1f, .4f, 1f) : new Color(.55f, .5f, .42f, .85f),
                    applyShapeToPosition = false
                }, 1);
            particles.Pause(false);
        }

        internal void Tick(float seconds)
        {
            if (seconds <= 0f) return;
            SimulationSeconds += seconds;
            foreach (Hole hole in holes)
                if (hole != null && hole.Transform.gameObject.activeSelf)
                {
                    if (hole.Surface == null || !hole.Surface.enabled || !hole.Surface.gameObject.activeInHierarchy)
                    { hole.Transform.gameObject.SetActive(false); hole.Surface = null; HoleCount--; }
                    else Refresh(hole);
                }
            if (ActiveParticleCount == 0) return;
            particles.Simulate(seconds, false, false, false);
            particles.Pause(false);
        }

        private void Refresh(Hole hole)
        {
            Transform surface = hole.Surface.transform;
            Vector3 normal = surface.worldToLocalMatrix.transpose.MultiplyVector(hole.LocalNormal).normalized;
            hole.Transform.SetPositionAndRotation(surface.TransformPoint(hole.LocalPoint) + normal * .0015f,
                Quaternion.AngleAxis(hole.Roll, normal) * Quaternion.FromToRotation(meshNormal, normal));
        }

        internal bool TryGetHole(int index, out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            foreach (Hole hole in holes)
                if (hole != null && hole.Surface != null && hole.Transform.gameObject.activeSelf && index-- == 0)
                {
                    point = hole.Transform.position;
                    normal = hole.Transform.TransformDirection(meshNormal).normalized;
                    return true;
                }
            return false;
        }

        internal void Clear()
        {
            if (particles != null) particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (Hole hole in holes)
                if (hole != null) { hole.Transform.gameObject.SetActive(false); hole.Surface = null; }
            HoleCount = EmissionCount = cursor = 0;
            SimulationSeconds = 0f; random = 0x738a21u;
        }

        private float Range(float from, float to)
        {
            random ^= random << 13; random ^= random >> 17; random ^= random << 5;
            return Mathf.Lerp(from, to, (random & 0xffffffu) / 16777215f);
        }

        private void OnDisable() => Clear();
    }
}
