using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Dense street drifts retain individual leaves. Only the nearest patches simulate and draw.</summary>
    [DefaultExecutionOrder(355), DisallowMultipleComponent]
    internal sealed class CityLeafField : MonoBehaviour
    {
        private const int VisiblePatches = 14;
        private const float ViewDistance = 31f;
        private static readonly Color32[] Pigments = {
            new(128, 102, 65, 255), new(117, 104, 67, 255), new(151, 117, 73, 255), new(161, 133, 78, 255)
        };

        private sealed class Patch
        {
            internal CityLeafSite Site;
            internal Vector3 Centre;
            internal Leaf[] Leaves;
            internal bool Visible;
            internal float NextShedding;
            internal int NextCrownLeaf;
        }

        internal sealed class Leaf
        {
            internal Vector3 Position, Velocity, Normal;
            internal FootstepGroundKind Floor;
            internal float Scale, Phase, Mass, Yaw, Roll, Wait, Compression, Shelter, LayerHeight;
            internal Transform HoldingFoot;
            internal Vector3 HoldOrigin, ToeOff;
            internal bool Placed, Flying, InCrown;
            internal int Pigment;
            internal float ReturnScale = 1f;
            internal Matrix4x4 Matrix;
        }

        private CityGrassSurface surface;
        private CityWindField wind;
        private Patch[] patches;
        private readonly List<Patch> nearby = new(VisiblePatches);
        private readonly Patch[] closest = new Patch[VisiblePatches];
        private readonly float[] distances = new float[VisiblePatches];
        private const int DrawBatchSize = 250;
        private readonly Matrix4x4[][] batches = new Matrix4x4[Pigments.Length][];
        private readonly int[] batchCounts = new int[Pigments.Length];
        private readonly Bounds[] batchBounds = new Bounds[Pigments.Length];
        private readonly RenderParams[] finishes = new RenderParams[Pigments.Length * 4];
        private Material material;
        private Mesh mesh;
        private Matrix4x4 importedTransform;
        private Vector3 lastEye;
        private Camera observer;
        private bool hasEye;
        private float nextSurvey;
        private int wetBand = -1;
        private static event Action<Transform, Vector3, Vector3> Contact;
        internal static bool HasContactListeners => Contact != null;
        internal static void Footfall(Transform foot, Vector3 ground, Vector3 velocity) => Contact?.Invoke(foot, ground, velocity);
        internal IReadOnlyList<CityLeafSite> Sites { get; private set; }
        internal IReadOnlyList<Leaf> LeavesAt(int site) => patches[site].Leaves ?? Array.Empty<Leaf>();
        internal int StepContacts { get; private set; }

        internal static CityLeafField Create(Transform parent, CityGrassSurface surface, CityWindField wind, List<CityLeafSite> sites)
        {
            var host = new GameObject("Windblown street leaves");
            host.transform.SetParent(parent, false);
            var field = host.AddComponent<CityLeafField>();
            field.surface = surface;
            field.wind = wind;
            field.Sites = sites;
            field.patches = new Patch[sites.Count];
            for (int i = 0; i < sites.Count; i++)
                field.patches[i] = new Patch { Site = sites[i], Centre = surface.FromCity(sites[i].Position) };
            var model = Resources.Load<GameObject>("City/Grass/JanitorLeaf").GetComponentInChildren<MeshFilter>();
            field.mesh = model.sharedMesh;
            field.importedTransform = model.transform.localToWorldMatrix;
            var texture = Resources.Load<Texture2D>("City/Grass/JanitorLeafAlbedo");
            field.material = new Material(RuntimePrimitiveFactory.DefaultMaterial)
            {
                name = "Fallen city leaves", hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = SystemInfo.supportsInstancing
            };
            field.material.SetTexture("_BaseMap", texture);
            for (int i = 0; i < field.batches.Length; i++)
                field.batches[i] = new Matrix4x4[DrawBatchSize];
            for (int i = 0; i < field.finishes.Length; i++)
            {
                float wet = (i / Pigments.Length) / 3f;
                Color color = Pigments[i % Pigments.Length];
                color = Color.Lerp(color, new Color(color.r * .70f, color.g * .68f, color.b * .70f), wet);
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", color);
                block.SetFloat("_Smoothness", Mathf.Lerp(.10f, .24f, wet));
                field.finishes[i] = new RenderParams(field.material)
                {
                    matProps = block, receiveShadows = true, shadowCastingMode = ShadowCastingMode.Off,
                    layer = host.layer
                };
            }
            if (surface.Home == null && field.isActiveAndEnabled) Contact += field.OnFootfall;
            return field;
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += Draw;
            if (surface != null && surface.Home == null) Contact += OnFootfall;
        }
        private void OnDisable()
        {
            Contact -= OnFootfall;
            RenderPipelineManager.beginCameraRendering -= Draw;
            foreach (var patch in nearby) Release(patch);
            nearby.Clear();
            hasEye = false;
        }

        private void OnDestroy()
        {
            if (material) Destroy(material);
        }

        private void Draw(ScriptableRenderContext context, Camera camera)
        {
            if (!hasEye || !camera || camera.cameraType == CameraType.Preview ||
                (camera.cullingMask & (1 << gameObject.layer)) == 0) return;
            foreach (var patch in nearby)
                foreach (var leaf in patch.Leaves)
                {
                    if (!leaf.Placed) continue;
                    int pigment = leaf.Pigment;
                    if (batchCounts[pigment] == 0) batchBounds[pigment] = new Bounds(leaf.Position, Vector3.zero);
                    else batchBounds[pigment].Encapsulate(leaf.Position);
                    batches[pigment][batchCounts[pigment]++] = leaf.Matrix;
                    if (batchCounts[pigment] == DrawBatchSize) DrawBatch(camera, pigment);
                }
            for (int pigment = 0; pigment < batches.Length; pigment++)
                if (batchCounts[pigment] != 0) DrawBatch(camera, pigment);
        }

        private void DrawBatch(Camera camera, int pigment)
        {
            var settings = finishes[wetBand * Pigments.Length + pigment];
            settings.camera = camera;
            var bounds = batchBounds[pigment];
            bounds.Expand(.40f);
            settings.worldBounds = bounds;
            int count = batchCounts[pigment];
            batchCounts[pigment] = 0;
            if (material.enableInstancing)
                Graphics.RenderMeshInstanced(settings, mesh, 0, batches[pigment], count);
            else
                for (int i = 0; i < count; i++) Graphics.RenderMesh(settings, mesh, 0, batches[pigment][i]);
        }

        private void LateUpdate()
        {
            if (patches == null || GameTimeScaleRuntime.IsPaused || Time.deltaTime <= 0f) return;
            if (!observer || !observer.isActiveAndEnabled) observer = Camera.main;
            if (!observer) return;
            Vector4 gust = wind.Wind;
            Advance(observer.transform.position, Time.deltaTime, new Vector3(gust.x, 0f, gust.y) * gust.z,
                CityWetSurfaceRegistry.CurrentWetness);
        }

        internal void Advance(Vector3 eye, float delta, Vector3 gust, float wet)
        {
            wetBand = Mathf.Clamp(Mathf.RoundToInt(wet * 3f), 0, 3);
            nextSurvey -= delta;
            if (!hasEye || (eye - lastEye).sqrMagnitude > 9f || nextSurvey <= 0f)
            {
                Survey(eye);
                lastEye = eye;
                hasEye = true;
                nextSurvey = .4f;
            }
            // Subdivide long render frames so a fast-moving leaf still meets a stair riser.
            float seconds = Mathf.Min(delta, .12f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(seconds * 30f));
            foreach (var patch in nearby)
            {
                ReplenishCrown(patch, seconds, wet);
                foreach (var leaf in patch.Leaves)
                {
                    if (!leaf.Placed) continue;
                    if (!leaf.InCrown && !leaf.Flying && !leaf.HoldingFoot && leaf.Compression == 0f &&
                        leaf.Velocity.sqrMagnitude < .0009f && leaf.Wait > seconds)
                    {
                        leaf.Wait -= seconds;
                        continue;
                    }
                    bool moved = false;
                    for (int i = 0; i < steps; i++)
                        moved |= Move(patch, leaf, gust, wet, seconds / steps);
                    if (moved) Pose(leaf);
                }
            }
        }

        private void ReplenishCrown(Patch patch, float seconds, float wet)
        {
            int count = patch.Site.CrownLeaves;
            if (count == 0) return;
            patch.NextShedding -= seconds;
            if (patch.NextShedding > 0f) return;
            var leaf = patch.Leaves[patch.NextCrownLeaf];
            if (!leaf.Placed || !leaf.InCrown && !leaf.Flying && !leaf.HoldingFoot && leaf.Velocity.sqrMagnitude < .0009f)
            {
                // A visible fallen leaf dwindles into the cover. A leaf lost over water can return directly.
                if (leaf.Placed)
                {
                    leaf.ReturnScale = Mathf.MoveTowards(leaf.ReturnScale, 0f, seconds * .8f);
                    if (leaf.ReturnScale > 0f) { Pose(leaf); return; }
                }
                leaf.Placed = true;
                leaf.Flying = false;
                leaf.Velocity = Vector3.zero;
                leaf.ReturnScale = 1f;
                leaf.Position = CrownPosition(patch, leaf);
                leaf.Wait = 1f + leaf.Mass * 3f;
                leaf.InCrown = true;
                leaf.Roll = 32f;
                Pose(leaf);
            }
            patch.NextCrownLeaf = (patch.NextCrownLeaf + 1) % count;
            patch.NextShedding = 1.7f + wet * 2f;
        }

        private Vector3 CrownPosition(Patch patch, Leaf leaf)
        {
            float reach = Mathf.Clamp(patch.Site.CrownHeight * .24f, .5f, 1.7f);
            Vector3 offset = new(Mathf.Cos(leaf.Phase) * reach,
                patch.Site.CrownHeight + Mathf.Sin(leaf.Phase * 1.7f) * reach * .35f,
                Mathf.Sin(leaf.Phase) * reach);
            return patch.Centre + surface.FromCityRotation * offset;
        }

        private void Survey(Vector3 eye)
        {
            int count = 0;
            foreach (var patch in patches)
            {
                float d = (patch.Centre - eye).sqrMagnitude;
                if (d > ViewDistance * ViewDistance || count == VisiblePatches && d >= distances[count - 1]) continue;
                int insert = Mathf.Min(count, VisiblePatches - 1);
                while (insert > 0 && distances[insert - 1] > d)
                {
                    closest[insert] = closest[insert - 1];
                    distances[insert] = distances[insert - 1];
                    insert--;
                }
                closest[insert] = patch;
                distances[insert] = d;
                count = Mathf.Min(count + 1, VisiblePatches);
            }
            for (int i = nearby.Count - 1; i >= 0; i--)
            {
                var old = nearby[i];
                if (Array.IndexOf(closest, old, 0, count) >= 0) continue;
                Release(old);
                nearby.RemoveAt(i);
            }
            for (int i = 0; i < count; i++)
            {
                var patch = closest[i];
                if (patch.Visible) continue;
                patch.Visible = true;
                nearby.Add(patch);
                if (patch.Leaves == null) Plant(patch);
            }
        }

        private void Plant(Patch patch)
        {
            var site = patch.Site;
            var random = new System.Random(unchecked((int)site.Seed));
            float Sample(float low, float high) => Mathf.Lerp(low, high, (float)random.NextDouble());
            patch.Leaves = new Leaf[site.Count];
            Vector3 anchor = patch.Centre;
            for (int i = 0; i < 10; i++)
            {
                float angle = Sample(0f, 2f * Mathf.PI);
                float distance = site.CrownHeight > 0f ? Sample(1.2f, 2.4f) : Sample(.08f, .50f);
                Vector3 candidate = patch.Centre + surface.FromCityRotation *
                    new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
                if (!Floor(candidate, 1.3f, 3.35f, out float y, out _, out _)) continue;
                candidate.y = y;
                if (Obstructed(candidate)) continue;
                anchor = candidate;
                break;
            }
            var axis = surface.FromCityRotation * Quaternion.Euler(0f, Sample(0f, 360f), 0f);
            float length = .60f * Mathf.Sqrt(site.Count / 48f);
            float width = length * Sample(.48f, .68f);
            if (site.Habitat == CityLeafHabitat.Birch) { length *= .79f; width *= .79f; }
            for (int i = 0; i < patch.Leaves.Length; i++)
            {
                bool crown = i < site.CrownLeaves;
                float size = site.Habitat == CityLeafHabitat.Birch ? Sample(.42f, .62f) : Sample(.68f, 1.08f);
                var leaf = new Leaf { Scale = size, Mass = Sample(.12f, .90f), Phase = Sample(0f, 6.28f),
                    Yaw = Sample(0f, 360f), Wait = Sample(.4f, 6.5f), Pigment = random.Next(Pigments.Length) };
                patch.Leaves[i] = leaf;
                if (crown)
                {
                    leaf.Placed = leaf.InCrown = true;
                    leaf.Position = CrownPosition(patch, leaf);
                    leaf.Roll = 32f;
                    Pose(leaf);
                    continue;
                }
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    float angle = Sample(0f, 2f * Mathf.PI);
                    float radius = Sample(.06f, 1f);
                    // Overlapping folds in the core, a ragged fringe and a few loose leaves in its lee.
                    bool fringe = i % 5 == 1;
                    float along = Mathf.Cos(angle) * radius + (fringe ? Sample(.55f, 1.45f) : 0f);
                    Vector3 at = anchor + axis * new Vector3(along * length, 0f, Mathf.Sin(angle) * radius * width);
                    if (!Floor(at, 1.35f, 3.35f, out float y, out leaf.Normal, out leaf.Floor)) continue;
                    at.y = y;
                    if (Obstructed(at)) continue;
                    leaf.Shelter = fringe ? 0f : 1f - radius * radius;
                    leaf.LayerHeight = leaf.Shelter * Sample(.007f, .044f);
                    at.y += .0035f + leaf.LayerHeight;
                    leaf.Position = at;
                    leaf.Placed = true;
                    Pose(leaf);
                    break;
                }
            }
        }

        private bool Obstructed(Vector3 floor) => surface.Home == null &&
            Physics.CheckSphere(floor + Vector3.up * .18f, .07f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        private static void Release(Patch patch)
        {
            patch.Visible = false;
            foreach (var leaf in patch.Leaves) leaf.HoldingFoot = null;
        }

        private void Pose(Leaf leaf)
        {
            Quaternion ground = Quaternion.FromToRotation(Vector3.up, leaf.Flying || leaf.InCrown ? Vector3.up : leaf.Normal);
            Vector3 scale = Vector3.one * (leaf.Scale * leaf.ReturnScale);
            scale.y *= 1f - .76f * leaf.Compression;
            leaf.Matrix = Matrix4x4.TRS(leaf.Position, ground * Quaternion.Euler(0f, leaf.Yaw, leaf.Roll), scale) * importedTransform;
        }

        private bool Move(Patch patch, Leaf leaf, Vector3 gust, float wet, float dt)
        {
            if (leaf.HoldingFoot)
            {
                var offset = leaf.HoldingFoot.position - leaf.HoldOrigin;
                if (leaf.HoldingFoot.gameObject.activeInHierarchy && offset.sqrMagnitude < .09f * .09f &&
                    offset.y < .05f) return false;
                leaf.HoldingFoot = null;
                leaf.Velocity = leaf.ToeOff;
                leaf.Wait = .7f;
            }
            bool recovering = leaf.Compression > 0f;
            leaf.Compression = Mathf.MoveTowards(leaf.Compression, 0f, dt * Mathf.Lerp(3.1f, .9f, wet));
            float strength = gust.magnitude;
            leaf.Wait -= dt;
            if (leaf.InCrown)
            {
                if (leaf.Wait > 0f || strength < .08f + wet * leaf.Mass * .48f) return false;
                leaf.InCrown = false;
                leaf.Flying = true;
                leaf.Velocity = gust * (1.2f + leaf.Mass);
            }
            if (!leaf.Flying && leaf.Velocity.sqrMagnitude < .0009f)
            {
                if (leaf.Wait > 0f) return recovering;
                leaf.Wait = 1.4f + leaf.Mass * 3.6f;
                var travel = leaf.Position - patch.Centre;
                if (strength < .14f + leaf.Mass * .42f + wet * .54f + leaf.Shelter * .98f ||
                    (travel.sqrMagnitude > 180f && Vector3.Dot(gust, travel) > 0f)) return recovering;
                leaf.Shelter = leaf.LayerHeight = 0f;
                leaf.Velocity = gust * (1.5f + (1f - leaf.Mass) * 1.5f);
                leaf.Velocity.y = .24f + .75f * (1f - wet) * (1f - leaf.Mass);
                leaf.Flying = true;
            }
            leaf.Phase += dt * (leaf.Flying ? 4.3f : 2.1f);
            var before = leaf.Position;
            if (leaf.Flying)
            {
                float flutter = Mathf.Sin(leaf.Phase);
                Vector3 cross = Vector3.Cross(Vector3.up, gust);
                Vector3 drift = gust * (patch.Site.Habitat == CityLeafHabitat.Fence ? 4.1f : 2.7f) +
                    cross * (.85f * flutter);
                float damping = 1f - Mathf.Exp(-dt * 1.6f);
                leaf.Velocity.x = Mathf.Lerp(leaf.Velocity.x, drift.x, damping);
                leaf.Velocity.z = Mathf.Lerp(leaf.Velocity.z, drift.z, damping);
                leaf.Velocity.y = Mathf.Max(-(.78f + wet * .65f + leaf.Mass * .35f),
                    leaf.Velocity.y - (2.8f + 1.4f * flutter) * dt);
                leaf.Roll = Mathf.MoveTowards(leaf.Roll, 67f * flutter, dt * 195f);
                leaf.Yaw += dt * (37f + 69f * Mathf.Cos(leaf.Phase * .63f));
            }
            else
            {
                float friction = leaf.Floor is FootstepGroundKind.Soil or FootstepGroundKind.Grass or
                    FootstepGroundKind.Sand ? 5.8f : 1.25f;
                leaf.Velocity = Vector3.MoveTowards(leaf.Velocity, Vector3.zero, (friction + wet * 1.45f) * dt);
                leaf.Yaw += leaf.Velocity.magnitude * dt * 86f;
                leaf.Roll = Mathf.MoveTowards(leaf.Roll, 0f, dt * 180f);
            }
            Vector3 next = before + leaf.Velocity * dt;
            if (surface.Home == null)
            {
                // A fence or a riser catches the leading edge before the root enters it.
                Vector3 run = new Vector3(next.x - before.x, 0f, next.z - before.z);
                if (run.sqrMagnitude > .000001f && Physics.Raycast(before + Vector3.up * .04f, run.normalized,
                    out var wall, run.magnitude + .035f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                    Mathf.Abs(wall.normal.y) < .65f)
                {
                    next.x = before.x; next.z = before.z;
                    leaf.Velocity = Vector3.ProjectOnPlane(leaf.Velocity, wall.normal) * .32f;
                }
            }
            float probeDepth = Mathf.Max(8f, patch.Site.CrownHeight + 3f);
            if (!Floor(next, .17f, probeDepth, out float top, out var normal, out var kind) ||
                top > before.y + (leaf.Flying ? .13f : .055f))
            {
                next.x = before.x; next.z = before.z;
                leaf.Velocity.x = leaf.Velocity.z = 0f;
                if (!Floor(next, .17f, probeDepth, out top, out normal, out kind))
                {
                    leaf.Placed = false;
                    return false;
                }
            }
            top += .0035f;
            if (leaf.Velocity.y <= 0f && next.y <= top + .012f || !leaf.Flying && before.y - top < .055f)
            {
                next.y = top;
                if (leaf.Flying)
                {
                    leaf.Velocity *= .66f;
                    leaf.Wait = 1.6f + leaf.Mass * 4.1f;
                }
                leaf.Velocity.y = 0f;
                leaf.Flying = false;
                leaf.Normal = normal;
                leaf.Floor = kind;
                leaf.Roll = Mathf.Clamp(leaf.Roll, -16f * leaf.Velocity.magnitude, 16f * leaf.Velocity.magnitude);
            }
            else leaf.Flying = true;
            leaf.Position = next;
            return true;
        }

        private readonly RaycastHit[] hits = new RaycastHit[16];

        private bool Floor(Vector3 at, float rise, float depth, out float y, out Vector3 normal, out FootstepGroundKind kind)
        {
            y = float.NegativeInfinity;
            normal = Vector3.up;
            kind = FootstepGroundKind.None;
            if (surface.LeafWater(at)) return false;
            if (surface.Home != null) return surface.TryLeafFloor(at, out y, out normal, out kind);
            var ray = new Ray(at + Vector3.up * rise, Vector3.down);
            int count = Physics.RaycastNonAlloc(ray, hits, depth, FootProbeSurface.ProbeMask, QueryTriggerInteraction.Collide);
            var buffer = count == hits.Length ? Physics.RaycastAll(ray, depth, FootProbeSurface.ProbeMask,
                QueryTriggerInteraction.Collide) : hits;
            if (buffer != hits) count = buffer.Length;
            bool tread = false;
            for (int i = 0; i < count; i++)
            {
                var hit = buffer[i];
                bool visibleTread = FootProbeSurface.IsProbeSurface(hit.collider);
                if (hit.normal.y < .63f || hit.collider is CharacterController ||
                    hit.collider.isTrigger && !visibleTread || hit.rigidbody != null) continue;
                // A walking ramp shares the stair footprint. Its individual drawn treads take precedence.
                if (hit.point.y < y && !(visibleTread && !tread && y - hit.point.y < .22f) ||
                    tread && !visibleTread && hit.point.y < y + .22f) continue;
                var stamp = hit.collider.GetComponentInParent<FootstepGround>();
                if (stamp == null && !visibleTread) continue;
                y = hit.point.y;
                normal = hit.normal;
                kind = stamp != null ? stamp.ResolveKind(hit.triangleIndex) : FootstepGroundKind.Stone;
                tread = visibleTread;
            }
            return !float.IsNegativeInfinity(y);
        }

        private void OnFootfall(Transform foot, Vector3 at, Vector3 velocity)
        {
            if (surface?.Home != null || !hasEye || GameTimeScaleRuntime.IsPaused) return;
            velocity.y = 0f;
            Vector3 forward = velocity.normalized;
            if (forward.sqrMagnitude < .01f) return;
            Vector3 side = Vector3.Cross(Vector3.up, forward);
            float wet = CityWetSurfaceRegistry.CurrentWetness;
            float speed = velocity.magnitude;
            float stride = Mathf.InverseLerp(.6f, 5f, speed);
            float halfWidth = Mathf.Lerp(.31f, .61f, stride);
            float reach = Mathf.Lerp(.51f, .93f, stride);
            float push = Mathf.Lerp(1.9f, 4.8f, stride) * (1f - .35f * wet);
            foreach (var patch in nearby)
                foreach (var leaf in patch.Leaves)
                {
                    if (!leaf.Placed || leaf.Flying || leaf.InCrown || leaf.HoldingFoot) continue;
                    Vector3 delta = leaf.Position - at;
                    float along = Vector3.Dot(delta, forward), across = Vector3.Dot(delta, side);
                    if (Mathf.Abs(delta.y) > .10f || along < -.19f || along > reach) continue;
                    float lateral = across / halfWidth;
                    float longitudinal = (along - .09f) / (along < .09f ? .29f : reach - .09f);
                    float edge = lateral * lateral + longitudinal * longitudinal;
                    if (edge > 1f) continue;
                    StepContacts++;
                    leaf.ReturnScale = 1f;
                    leaf.Shelter = 0f;
                    leaf.Position.y -= leaf.LayerHeight;
                    leaf.LayerHeight = 0f;
                    leaf.Wait = 1.1f;
                    if (along > -.12f && along < .075f && Mathf.Abs(across) < .067f)
                    {
                        leaf.HoldingFoot = foot;
                        leaf.HoldOrigin = foot ? foot.position : at;
                        leaf.ToeOff = (forward + side * (.45f * Mathf.Sin(leaf.Phase))) * (.32f * push);
                        leaf.Compression = 1f;
                        leaf.Roll = 0f;
                        leaf.Velocity = Vector3.zero;
                    }
                    else
                    {
                        float falloff = 1f - .65f * edge;
                        float energy = push * falloff * Mathf.Lerp(1.18f, .82f, leaf.Mass);
                        float fan = lateral * 2.25f + .30f * Mathf.Sin(leaf.Phase);
                        leaf.Velocity = (forward * Mathf.Lerp(.7f, 1.15f, Mathf.Clamp01(along / reach)) + side * fan).normalized * energy;
                        leaf.Velocity.y = energy * (.44f + .37f * (1f - leaf.Mass)) * (1f - wet);
                        leaf.Flying = leaf.Velocity.y > .22f;
                        if (!leaf.Flying) leaf.Velocity.y = 0f;
                        leaf.Roll = (fan < 0f ? -1f : 1f) * (18f + 31f * falloff);
                    }
                    Pose(leaf);
                }
        }
    }
}
