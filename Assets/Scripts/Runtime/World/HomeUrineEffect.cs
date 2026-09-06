using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>World-space ballistic liquid with bounded visuals, swept impacts and session-persistent wet surfaces.</summary>
    public sealed class HomeUrineEffect : MonoBehaviour
    {
        public const int PacketCapacity = 160;
        public const int SplashCapacity = 40;
        public const float PacketsPerSecond = 120f;
        public const float StreamSpeed = 4.2f;
        private sealed class Visual
        {
            public Transform Transform;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
        }
        private sealed class Packet
        {
            public Visual Visual;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Age;
            public float Diameter;
            public bool Active;
            public bool Drop;
        }
        private sealed class Splash
        {
            public Visual Visual;
            public float Age;
            public bool Active;
        }
        private sealed class Stain
        {
            public Visual Visual;
            public Mesh Mesh;
            public int Revision = -1;
            public bool Wall;
            public bool HasTemplate;
            public Vector3[] Vertices;
        }

        // The render pipeline's own "not for the GPU Resident Drawer" marker; internal to the
        // GPUDriven runtime assembly, so it is found by full name across the loaded assemblies.
        private static readonly Type DisallowGpuDrivenRenderingType =
            FindBehaviourType("UnityEngine.Rendering.DisallowGPUDrivenRendering");

        private static Type FindBehaviourType(string fullName)
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type candidate = assembly.GetType(fullName, false);
                if (candidate != null && typeof(MonoBehaviour).IsAssignableFrom(candidate)) return candidate;
            }
            return null;
        }

        private readonly Packet[] packets = new Packet[PacketCapacity];
        private readonly Splash[] splashes = new Splash[SplashCapacity];
        private readonly Stain[] stains = new Stain[HomeUrineResidue.Capacity];
        private HomeUrineSurfaceMap surfaces;
        private Transform receiverRoot;
        private Mesh segmentMesh;
        private Mesh dropMesh;
        private Mesh splashMesh;
        private Mesh stainMesh;
        private Mesh wallStainMesh;
        private Vector3[] stainVertices;
        private Vector3[] wallStainVertices;
        private AudioSource bowlAudio;
        private AudioSource solidAudio;
        private float bowlSound;
        private float solidSound;
        // Fractional packets, not elapsed seconds: changing pressure cannot re-price accumulated time.
        private float emissionRemainder;
        private int nextPacket;
        private int nextSplash;
        private int residueGeneration;
        private int mapFrame = -1;
        private bool initialized;
        private bool emissionEnabled;
        private bool emissionShaking;

        public int EmittedPacketCount { get; private set; }
        public int SurfaceHitCount { get; private set; }
        public int BowlHitCount { get; private set; }
        // Resolved settings applied by the latest valid emission step, including zero flow.
        public float LastEmissionFlow { get; private set; }
        public float LastEmissionSpeed { get; private set; }
        public float LastEmissionDiameter { get; private set; }
        public float LastEmissionRate { get; private set; }
        public Vector3 LastHitPoint { get; private set; }
        public string LastHitSurfaceId { get; private set; }
        public int ResidueCount => HomeUrineResidue.Deposits.Count;
        /// <summary>The pipeline's GPU-driven opt-out marker was found and rides every visual.</summary>
        public static bool GpuDrivenOptOutAvailable => DisallowGpuDrivenRenderingType != null;
        public int ReceiverCount => surfaces != null ? surfaces.Count : 0;
        public int ActivePacketCount
        {
            get { int count = 0; foreach (Packet packet in packets) if (packet != null && packet.Active) count++; return count; }
        }
        public static void ResetSession() => HomeUrineResidue.ResetSession();

        public void Initialize(Transform homeRoot)
        {
            if (initialized) return;
            if (homeRoot == null) throw new ArgumentNullException(nameof(homeRoot));
            segmentMesh = HomeUrineResources.Mesh("StreamSegment");
            dropMesh = HomeUrineResources.Mesh("Droplet");
            splashMesh = HomeUrineResources.Mesh("Splash");
            stainMesh = HomeUrineResources.Mesh("Stain");
            wallStainMesh = HomeUrineResources.Mesh("WallStain");
            stainVertices = stainMesh.vertices;
            wallStainVertices = wallStainMesh.vertices;
            HomeInteriorRoot home = homeRoot.GetComponent<HomeInteriorRoot>();
            receiverRoot = home != null && home.Room != null ? home.Room : homeRoot;
            surfaces = new HomeUrineSurfaceMap(receiverRoot, transform);
            for (int i = 0; i < packets.Length; i++)
                packets[i] = new Packet { Visual = CreateVisual("Urine Packet " + i, segmentMesh, HomeUrineResources.Liquid) };
            for (int i = 0; i < splashes.Length; i++)
                splashes[i] = new Splash { Visual = CreateVisual("Urine Splash " + i, splashMesh, HomeUrineResources.Liquid) };
            bowlAudio = CreateAudio("Urine Bowl Contact", HomeUrineResources.Bowl);
            solidAudio = CreateAudio("Urine Solid Contact", HomeUrineResources.Solid);
            residueGeneration = HomeUrineResidue.Generation;
            initialized = true;
            RefreshSurfaces();
            RefreshResidue();
        }

        public void BeginEmission()
        {
            if (!initialized) return;
            // Exit/door furniture and later calendar dressing are composed after this effect initializes.
            surfaces = new HomeUrineSurfaceMap(receiverRoot, transform);
            mapFrame = -1;
            emissionEnabled = true;
            emissionShaking = false;
            emissionRemainder = 0f;
            EmittedPacketCount = SurfaceHitCount = BowlHitCount = 0;
            LastEmissionFlow = LastEmissionSpeed = LastEmissionDiameter = LastEmissionRate = 0f;
            LastHitSurfaceId = null;
        }

        /// <summary>The caller supplies the exact action-time portion of this frame. Old packets retain their own velocity.</summary>
        public void EmitStep(Vector3 outlet, Vector3 direction, float deltaTime, float flow, bool shaking)
        {
            if (!initialized || !emissionEnabled || deltaTime <= 0f) return;
            flow = Mathf.Clamp01(flow);
            float pressure = Mathf.Sqrt(flow);
            float speed = shaking ? 0.75f : StreamSpeed * pressure;
            float diameter = shaking ? 0.004f : 0.003f * pressure;
            float rate = (shaking ? 10f : PacketsPerSecond) * flow;
            LastEmissionFlow = flow;
            LastEmissionSpeed = speed;
            LastEmissionDiameter = diameter;
            LastEmissionRate = rate;
            if (shaking != emissionShaking)
            {
                emissionRemainder = 0f;
                emissionShaking = shaking;
            }
            if (flow <= 0f) return;
            RefreshSurfaces();
            float interval = 1f / rate;
            float first = (1f - emissionRemainder) * interval;
            emissionRemainder += deltaTime * rate;
            int count = Mathf.FloorToInt(emissionRemainder);
            emissionRemainder -= count;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            for (int i = 0; i < count; i++)
            {
                Packet packet = packets[nextPacket++ % PacketCapacity];
                float phase = (EmittedPacketCount + 1) * 2.3999632f;
                Vector3 side = Vector3.Cross(direction, Vector3.up);
                if (side.sqrMagnitude < 0.01f) side = Vector3.right;
                Vector3 variation = side.normalized * Mathf.Sin(phase) + Vector3.up * Mathf.Cos(phase);
                packet.Position = outlet;
                packet.Velocity = direction * speed + variation * (shaking ? 0.25f : 0.027f * pressure);
                packet.Age = 0f;
                packet.Drop = shaking;
                packet.Diameter = diameter;
                packet.Active = true;
                packet.Visual.Filter.sharedMesh = shaking ? dropMesh : segmentMesh;
                packet.Visual.Renderer.enabled = true;
                EmittedPacketCount++;
                // A hitch emits along the elapsed interval, not a lump at its terminal instant.
                SimulatePacket(packet, Mathf.Max(0f, deltaTime - first - i * interval));
                if (packet.Active) PlacePacket(packet);
            }
            RefreshResidue();
        }

        public void StopEmission() { emissionEnabled = false; emissionRemainder = 0f; }

        private void Update()
        {
            if (!initialized) return;
            float seconds = Mathf.Max(0f, Time.deltaTime);
            RefreshSurfaces();
            foreach (Packet packet in packets)
                if (packet.Active) { SimulatePacket(packet, seconds); if (packet.Active) PlacePacket(packet); }
            foreach (Splash splash in splashes)
            {
                if (!splash.Active) continue;
                splash.Age += seconds;
                if (splash.Age >= 0.14f) { splash.Active = false; splash.Visual.Renderer.enabled = false; }
                else splash.Visual.Transform.localScale = Vector3.one * Mathf.Lerp(0.007f, 0.04f, splash.Age / 0.14f);
            }
            float fade = Mathf.Exp(-seconds * 18f);
            bowlSound *= fade; solidSound *= fade;
            UpdateAudio(bowlAudio, bowlSound * 0.5f);
            UpdateAudio(solidAudio, solidSound * 0.36f);
            RefreshResidue();
        }

        private void SimulatePacket(Packet packet, float seconds)
        {
            while (seconds > 0.000001f && packet.Active)
            {
                float step = Mathf.Min(HomeUrineTrajectory.MaximumStep, seconds);
                Vector3 old = packet.Position;
                HomeUrineTrajectory.Advance(ref packet.Position, ref packet.Velocity, step);
                packet.Age += step;
                if (surfaces.Cast(old, packet.Position, out HomeUrineSurfaceMap.Hit hit))
                {
                    HitSurface(hit, packet.Drop);
                    packet.Active = false;
                }
                else if (packet.Age >= 3f || packet.Position.y < -12f) packet.Active = false;
                seconds -= step;
            }
            if (!packet.Active) packet.Visual.Renderer.enabled = false;
        }

        private void HitSurface(HomeUrineSurfaceMap.Hit hit, bool drop)
        {
            LastHitPoint = hit.Point;
            LastHitSurfaceId = hit.Surface.Id;
            if (hit.Surface.Absorbs)
            { BowlHitCount++; bowlSound = drop ? 0.22f : 1f; bowlAudio.transform.position = hit.Point; }
            else
            { SurfaceHitCount++; HomeUrineResidue.Add(hit, drop ? 0.2f : 1f); solidSound = drop ? 0.22f : 1f; solidAudio.transform.position = hit.Point; }
            Splash splash = splashes[nextSplash++ % SplashCapacity];
            splash.Active = true; splash.Age = 0f;
            splash.Visual.Transform.SetPositionAndRotation(hit.Point + hit.Normal * 0.003f, SurfaceRotation(hit.Normal));
            splash.Visual.Transform.localScale = Vector3.one * 0.007f;
            splash.Visual.Renderer.enabled = true;
        }

        private void PlacePacket(Packet packet)
        {
            Mesh mesh = packet.Drop ? dropMesh : segmentMesh;
            float length = packet.Drop ? packet.Diameter * 1.5f : Mathf.Max(0.015f, packet.Velocity.magnitude / PacketsPerSecond * 1.22f);
            Quaternion rotation = Quaternion.LookRotation(packet.Velocity.normalized);
            Vector3 scale = new Vector3(packet.Diameter, packet.Diameter, length);
            packet.Visual.Transform.SetPositionAndRotation(packet.Position - rotation * Vector3.Scale(mesh.bounds.center, scale), rotation);
            packet.Visual.Transform.localScale = scale;
        }

        private void RefreshSurfaces()
        {
            if (mapFrame == Time.frameCount) return;
            surfaces.Refresh();
            mapFrame = Time.frameCount;
        }

        private void RefreshResidue()
        {
            if (residueGeneration != HomeUrineResidue.Generation)
            {
                foreach (Stain old in stains) if (old != null) { old.Visual.Renderer.enabled = false; old.Revision = -1; }
                residueGeneration = HomeUrineResidue.Generation;
            }
            for (int index = 0; index < HomeUrineResidue.Deposits.Count; index++)
            {
                HomeUrineResidue.Deposit deposit = HomeUrineResidue.Deposits[index];
                if (!surfaces.TryGet(deposit.SurfaceId, out HomeUrineSurfaceMap.Surface surface) || surface.Transform == null) continue;
                Stain stain = stains[index];
                if (stain == null)
                {
                    stain = new Stain { Mesh = new Mesh { name = "Home Urine Attached Film " + index } };
                    stain.Mesh.MarkDynamic();
                    stain.Visual = CreateVisual("Urine Stain " + index, stain.Mesh, HomeUrineResources.Residue);
                    stains[index] = stain;
                }
                if (stain.Revision == deposit.Revision) continue;
                stain.Revision = deposit.Revision;
                ProjectStain(stain, deposit, surface);
            }
        }

        private void ProjectStain(Stain stain, HomeUrineResidue.Deposit deposit, HomeUrineSurfaceMap.Surface surface)
        {
            Mesh template = deposit.Wall ? wallStainMesh : stainMesh;
            Vector3[] authored = deposit.Wall ? wallStainVertices : stainVertices;
            if (!stain.HasTemplate || stain.Wall != deposit.Wall)
            {
                stain.Mesh.Clear();
                stain.Vertices = new Vector3[authored.Length];
                stain.Mesh.vertices = authored;
                stain.Mesh.triangles = template.triangles;
                stain.Mesh.uv = template.uv;
                stain.HasTemplate = true;
                stain.Wall = deposit.Wall;
            }
            Transform receiver = surface.Transform;
            Vector3 center = receiver.TransformPoint(deposit.LocalPoint);
            Vector3 normal = receiver.worldToLocalMatrix.transpose.MultiplyVector(deposit.LocalNormal).normalized;
            Quaternion rotation = SurfaceRotation(normal);
            float diameter = deposit.Radius * 2f;
            for (int i = 0; i < authored.Length; i++)
            {
                Vector3 vertex = authored[i];
                Vector3 desired = center + rotation * new Vector3(vertex.x * diameter, vertex.y * diameter, 0f);
                HomeUrineSurfaceMap.Hit projected = default;
                bool found = false;
                // Retract boundary vertices onto their own receiver, never a neighbouring floor or wall.
                for (int attempt = 0; attempt < 5 && !found; attempt++)
                {
                    Vector3 sample = Vector3.Lerp(center, desired, 1f - attempt * 0.25f);
                    found = surface.Cast(sample + normal * 0.10f, sample - normal * 0.10f, out projected) && Vector3.Dot(projected.Normal, normal) > 0.45f;
                }
                float filmOffset = 0.0018f + vertex.z * Mathf.Max(0.1f, diameter);
                Vector3 point = found ? projected.Point + projected.Normal * filmOffset : center + normal * filmOffset;
                stain.Vertices[i] = receiver.InverseTransformPoint(point);
            }
            stain.Visual.Transform.SetParent(receiver, false);
            stain.Visual.Transform.localPosition = Vector3.zero;
            stain.Visual.Transform.localRotation = Quaternion.identity;
            stain.Visual.Transform.localScale = Vector3.one;
            stain.Mesh.vertices = stain.Vertices;
            stain.Mesh.RecalculateNormals();
            stain.Mesh.RecalculateBounds();
            stain.Visual.Renderer.enabled = true;
        }

        private static Quaternion SurfaceRotation(Vector3 normal)
        {
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (up.sqrMagnitude < 0.01f) up = Vector3.ProjectOnPlane(Vector3.forward, normal);
            return Quaternion.LookRotation(normal, up.normalized);
        }

        private Visual CreateVisual(string name, Mesh mesh, Material material)
        {
            var result = new GameObject(name);
            result.transform.SetParent(transform, false);
            MeshFilter filter = result.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
            MeshRenderer renderer = result.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            // These meshes are cleared, refilled and destroyed on the CPU
            // while their renderers live; the GPU Resident Drawer keeps a
            // registered mesh handle across such rebuilds and then submits
            // draw commands against an invalid mesh ("BatchDrawCommand was
            // submitted with an invalid ... Mesh ID"). Keep them on the
            // classic path: the opt-out flag is internal to the render
            // pipeline package, so its own component sets it for us.
            if (DisallowGpuDrivenRenderingType != null) result.AddComponent(DisallowGpuDrivenRenderingType);
            renderer.enabled = false;
            return new Visual { Transform = result.transform, Filter = filter, Renderer = renderer };
        }

        private AudioSource CreateAudio(string name, AudioClip clip)
        {
            var result = new GameObject(name);
            result.transform.SetParent(transform, false);
            AudioSource audio = result.AddComponent<AudioSource>();
            audio.clip = clip; audio.loop = true; audio.playOnAwake = false;
            audio.spatialBlend = 1f; audio.minDistance = 0.6f; audio.maxDistance = 5f;
            audio.rolloffMode = AudioRolloffMode.Linear; audio.dopplerLevel = 0f; audio.volume = 0f;
            GameAudioMixer.Route(audio, GameAudioGroup.SfxWorld);
            return audio;
        }

        private static void UpdateAudio(AudioSource audio, float volume)
        {
            audio.volume = volume;
            if (volume > 0.002f && !audio.isPlaying) audio.Play();
            else if (volume <= 0.002f && audio.isPlaying) audio.Stop();
        }

        private void OnDisable()
        {
            StopEmission();
            foreach (Packet packet in packets) if (packet != null) { packet.Active = false; packet.Visual.Renderer.enabled = false; }
            foreach (Splash splash in splashes) if (splash != null) { splash.Active = false; splash.Visual.Renderer.enabled = false; }
            bowlSound = solidSound = 0f;
            if (bowlAudio != null) bowlAudio.Stop();
            if (solidAudio != null) solidAudio.Stop();
        }

        private void OnDestroy()
        {
            foreach (Stain stain in stains)
            {
                if (stain == null) continue;
                // A renderer must never outlive its mesh, even for the
                // remainder of this frame.
                if (stain.Visual.Renderer != null) stain.Visual.Renderer.enabled = false;
                if (stain.Visual.Transform != null) Destroy(stain.Visual.Transform.gameObject);
                if (stain.Mesh != null) Destroy(stain.Mesh);
            }
        }
    }
}
