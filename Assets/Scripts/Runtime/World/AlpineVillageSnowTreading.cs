using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The snow remembers where he walked, and says so under his feet.
    ///
    /// Deformation here is CPU-side and deliberately so. The industry answer
    /// is a top-down accumulation RenderTexture read by a vertex shader, and
    /// this project cannot afford its price: `Ps1Lit` is a verbatim copy of
    /// URP Lit that must stay re-copyable on a URP bump, so displacement
    /// would mean a THIRD clone to maintain, and the snapped clip position
    /// would quantise exactly the sub-decimetre amplitude a footprint is made
    /// of. The snow is already its own mesh with its own pure depth field, so
    /// pressing it down is one float per vertex and a throttled re-upload.
    ///
    /// What reads at `640x360` is a GROOVE, not tread detail. So the stamp is
    /// wide and soft rather than boot-shaped, and the mesh it works on is
    /// pitched for that: `RibbonCrossStep` across a route and
    /// `FieldCellSize` away from one.
    ///
    /// Nothing here touches collision. The hero walks the same flat ground he
    /// always did - planar velocity is read back from achieved movement, so
    /// snow he could catch a boot on would read as a crawl - and the trail is
    /// what the eye gets instead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AlpineVillageSnowTreading
        : MonoBehaviour, IPlayerFootstepSurface
    {
        /// <summary>
        /// How wide a pass presses the snow. Wider than a boot on purpose:
        /// what survives the resolution is the trail, and a stamp narrower
        /// than <see cref="AlpineVillageSnowDrift.RibbonCrossStep"/> would
        /// fall between vertices and press nothing at all.
        /// </summary>
        public const float TreadRadius = 0.55f;

        /// <summary>How far down a single pass takes the snow, as a fraction
        /// of its own depth.</summary>
        public const float TreadStrength = 0.85f;

        /// <summary>
        /// How fast the gale fills a trail back in, per second, at full
        /// snowfall. The village never stops snowing - `SnowFloor` is `0.88`
        /// - so a trail is minutes old at most, and that is the honest
        /// lifetime rather than a budget.
        /// </summary>
        public const float RefillPerSecond = 0.035f;

        /// <summary>How often the pressed mesh is re-uploaded. Normals cost
        /// more than the vertices do, so this is a rate rather than a
        /// per-frame job.</summary>
        public const float RebuildInterval = 0.1f;

        /// <summary>Depth under which the ground is bare enough that a step
        /// sounds like earth rather than snow.</summary>
        public const float BareStepDepth = 0.06f;

        private Mesh mesh;
        private Vector3[] vertices;
        private float[] grounds;
        private float[] depths;
        private float[] pressed;
        private Transform walker;
        private Func<float> snowfall;
        private float rebuildCountdown;
        private bool dirty;
        private Vector3 lastStamp = new Vector3(float.NaN, 0f, 0f);

        /// <summary>Depth of lying snow under the walker last time it was
        /// sampled, for anything that wants to know what he is standing in.
        /// </summary>
        public float DepthUnderWalker { get; private set; }

        /// <summary>
        /// Gives the snow the hero to follow and the snowfall that fills his
        /// trail back in. Separate from <see cref="Initialize"/> because the
        /// world builder has no player and must not wait for one.
        /// </summary>
        public void AttachWalker(
            Transform walkerToFollow,
            Func<float> snowfallIntensity)
        {
            walker = walkerToFollow;
            snowfall = snowfallIntensity;
        }

        public void Initialize(
            Mesh snowMesh,
            Vector3[] snowVertices,
            float[] groundHeights,
            float[] snowDepths,
            Transform walkerToFollow,
            Func<float> snowfallIntensity)
        {
            mesh = snowMesh ??
                   throw new ArgumentNullException(nameof(snowMesh));
            vertices = snowVertices ??
                       throw new ArgumentNullException(nameof(snowVertices));
            grounds = groundHeights ??
                      throw new ArgumentNullException(nameof(groundHeights));
            depths = snowDepths ??
                     throw new ArgumentNullException(nameof(snowDepths));
            if (vertices.Length != grounds.Length ||
                vertices.Length != depths.Length)
            {
                throw new ArgumentException(
                    "The snow mesh and its ground/depth arrays disagree.");
            }

            pressed = new float[vertices.Length];
            walker = walkerToFollow;
            snowfall = snowfallIntensity;
        }

        /// <summary>
        /// Presses the snow under one world point, as a foot would. Exposed
        /// so a test can walk a line without a player rig.
        /// </summary>
        public void Press(Vector3 worldPosition)
        {
            if (vertices == null)
            {
                return;
            }

            float radiusSquared = TreadRadius * TreadRadius;
            for (int index = 0; index < vertices.Length; index++)
            {
                if (depths[index] <= 0f)
                {
                    continue;
                }

                float dx = vertices[index].x - worldPosition.x;
                float dz = vertices[index].z - worldPosition.z;
                float distanceSquared = dx * dx + dz * dz;
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                // Soft-edged, so a pass leaves a groove rather than a stamped
                // circle: full strength under the foot, nothing at the rim.
                float falloff = 1f - Mathf.Sqrt(distanceSquared) / TreadRadius;
                float target = TreadStrength *
                               Mathf.SmoothStep(0f, 1f, falloff);
                if (target <= pressed[index])
                {
                    continue;
                }

                pressed[index] = target;
                dirty = true;
            }
        }

        public bool TryPlayFootstep(Vector3 position, float runBlend)
        {
            if (vertices == null)
            {
                return false;
            }

            float depth = SampleVisibleDepth(position);
            DepthUnderWalker = depth;
            RetroAudio.PlayAt(
                depth >= BareStepDepth
                    ? RetroSfxId.FootstepSnow
                    : RetroSfxId.FootstepSoil,
                position);
            if (depth >= BareStepDepth)
            {
                AlpineVillageSnowKickup.Spawn(transform, position, depth);
            }

            return true;
        }

        private void Update()
        {
            if (vertices == null)
            {
                return;
            }

            if (walker != null)
            {
                Vector3 at = walker.position;
                // Only when he has actually moved: standing still should not
                // drill a hole, and re-pressing the same ring every frame is
                // the whole cost of this for nothing.
                if (float.IsNaN(lastStamp.x) ||
                    (at - lastStamp).sqrMagnitude > 0.04f)
                {
                    Press(at);
                    lastStamp = at;
                }
            }

            Refill(Time.deltaTime);
            rebuildCountdown -= Time.deltaTime;
            if (dirty && rebuildCountdown <= 0f)
            {
                Rebuild();
                rebuildCountdown = RebuildInterval;
            }
        }

        private void Refill(float deltaTime)
        {
            float intensity = snowfall == null
                ? 1f
                : Mathf.Clamp01(snowfall());
            float step = RefillPerSecond * intensity * deltaTime;
            if (step <= 0f)
            {
                return;
            }

            for (int index = 0; index < pressed.Length; index++)
            {
                if (pressed[index] <= 0f)
                {
                    continue;
                }

                pressed[index] = Mathf.Max(0f, pressed[index] - step);
                dirty = true;
            }
        }

        private void Rebuild()
        {
            for (int index = 0; index < vertices.Length; index++)
            {
                vertices[index].y = grounds[index] +
                                    depths[index] *
                                    (1f - pressed[index]);
            }

            mesh.SetVertices(vertices);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            dirty = false;
        }

        /// <summary>
        /// The pressed depth at a world point, taken from the nearest vertex
        /// rather than from the plan: what matters to a footstep is what the
        /// eye can see there, and after a few passes those differ.
        ///
        /// Public because it is the only honest way to ask "how deep is the
        /// snow he is actually standing in" - the plan's own field cannot
        /// answer it once anything has walked.
        /// </summary>
        public float SampleVisibleDepth(Vector3 position)
        {
            float bestSquared = float.PositiveInfinity;
            float depth = 0f;
            for (int index = 0; index < vertices.Length; index++)
            {
                float dx = vertices[index].x - position.x;
                float dz = vertices[index].z - position.z;
                float distanceSquared = dx * dx + dz * dz;
                if (distanceSquared >= bestSquared)
                {
                    continue;
                }

                bestSquared = distanceSquared;
                depth = depths[index] * (1f - pressed[index]);
            }

            // Accepted out to the COARSEST spacing the snow is sampled at,
            // not to the tread radius: the field sheet is on a `1 m` grid, so
            // a tread-sized window finds no vertex at all across most of the
            // bowl and every step out there would sound like bare earth.
            float reach = Mathf.Max(
                TreadRadius,
                AlpineVillageSnowDrift.FieldCellSize);
            return bestSquared <= reach * reach ? depth : 0f;
        }
    }
}
