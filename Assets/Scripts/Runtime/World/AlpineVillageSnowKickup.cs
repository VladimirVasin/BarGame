using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The snow a boot throws forward, for about a third of a second.
    ///
    /// Small solid motes rather than a sprite: the ban on flat untextured
    /// quads near anything the player walks up to is a standing order, and a
    /// billboard at ankle height beside a door is exactly the thing it names.
    /// They are lit by the same snow sheet as the ground they came out of, so
    /// nothing here needs a material of its own.
    ///
    /// Deliberately tiny and deliberately brief. At `640x360` a step's spray
    /// is two or three pixels moving; anything bigger stops reading as snow
    /// and starts reading as an effect.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AlpineVillageSnowKickup : MonoBehaviour
    {
        public const int MoteCount = 5;
        public const float LifeSeconds = 0.34f;
        public const float MoteSize = 0.045f;
        public const float RiseSpeed = 0.85f;
        public const float SpreadSpeed = 0.65f;

        private Transform[] motes;
        private Vector3[] velocities;
        private float age;
        private float activeMoteSize = MoteSize;
        private float activeLifeSeconds = LifeSeconds;

        /// <summary>
        /// Throws one step's worth of snow at a point. Depth scales it, so a
        /// boot through the shallow skirt beside a path barely marks the air
        /// and one in the open field throws properly.
        /// </summary>
        public static void Spawn(
            Transform parent,
            Vector3 position,
            float depth)
        {
            Spawn(parent, position, depth, sand: false);
        }

        /// <summary>
        /// Reuses the same brief solid grains for the beach. Damp sand
        /// throws smaller, lower grains and carries the shore's own sheet.
        /// </summary>
        public static void Spawn(
            Transform parent,
            Vector3 position,
            float depth,
            bool sand)
        {
            if (depth <= 0f)
            {
                return;
            }

            var host = new GameObject(sand ? "Sand Kickup" : "Snow Kickup");
            host.transform.SetParent(parent, false);
            host.transform.position = position;
            AlpineVillageSnowKickup kickup =
                host.AddComponent<AlpineVillageSnowKickup>();
            kickup.Build(Mathf.Clamp01(depth / (sand ? 0.12f : 0.45f)), sand);
        }

        private void Build(float strength, bool sand)
        {
            activeMoteSize = sand ? 0.025f : MoteSize;
            activeLifeSeconds = sand ? 0.26f : LifeSeconds;
            float rise = sand ? 0.48f : RiseSpeed;
            float spread = sand ? 0.40f : SpreadSpeed;
            motes = new Transform[MoteCount];
            velocities = new Vector3[MoteCount];
            for (int index = 0; index < MoteCount; index++)
            {
                // Fanned by index rather than by Random, so a step looks the
                // same every time it is captured and a frame comparison can
                // be trusted.
                float turn = index * (360f / MoteCount) + strength * 40f;
                Vector3 out3 = Quaternion.Euler(0f, turn, 0f) *
                               Vector3.forward;
                GameObject mote = RuntimePrimitiveFactory.CreateBox(
                    $"Mote {index:00}",
                    transform,
                    out3 * (activeMoteSize * 1.5f) +
                    Vector3.up * (activeMoteSize * 0.5f),
                    Vector3.one * (activeMoteSize * Mathf.Lerp(0.6f, 1f, strength)),
                    Color.white,
                    false);
                // Primitive collider removal is deferred until end of frame.
                // Grains must already be nonphysical on their spawn frame.
                Collider pendingCollider = mote.GetComponent<Collider>();
                if (pendingCollider != null) pendingCollider.enabled = false;
                if (sand)
                {
                    CitySeacoastSurfaceAppearance.ApplyCombined(
                        mote.GetComponent<Renderer>(),
                        CitySeacoastSurfaceKind.Sand,
                        CityExteriorAppearance.BeachSand);
                }
                else
                {
                    MountainRoadSurfaceAppearance.Apply(
                        mote.GetComponent<Renderer>(),
                        AlpineVillageRidgeAppearance.Surface,
                        Color.white);
                }
                mote.GetComponent<Renderer>().shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                motes[index] = mote.transform;
                velocities[index] =
                    out3 * (spread * strength) +
                    Vector3.up * (rise * strength);
            }
        }

        private void Update()
        {
            age += Time.deltaTime;
            if (age >= activeLifeSeconds || motes == null)
            {
                Destroy(gameObject);
                return;
            }

            float remaining = 1f - age / activeLifeSeconds;
            for (int index = 0; index < motes.Length; index++)
            {
                Transform mote = motes[index];
                if (mote == null)
                {
                    continue;
                }

                velocities[index] += Vector3.down * (3.4f * Time.deltaTime);
                mote.position += velocities[index] * Time.deltaTime;
                // Shrinking rather than fading: the shared snow material is
                // opaque, and a per-instance alpha would need a material of
                // its own for five cubes that live a third of a second.
                mote.localScale = Vector3.one *
                                  (activeMoteSize * remaining * remaining);
            }
        }
    }
}
