using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What a floor sounds like under a boot. Material, not geometry: the
    /// shape of the ground under a foot is <c>FootSurfaceKind</c>'s business.
    /// </summary>
    public enum FootstepGroundKind
    {
        None = 0,
        Concrete,
        Stone,
        Grass,
        Soil,
        Sand,
        Snow,
        Wood,
        Carpet,
        Tile,
        Puddle
    }

    /// <summary>
    /// The marker a world builder stamps on walkable collision so the hero's
    /// footstep can read the floor it lands on.
    ///
    /// Nothing else on a built ground object says what it is made of: there
    /// are no tags, no physics materials, and the material is a shared
    /// primitive with a property block. So the builder that knows - the one
    /// that chose the texture - says so once, on the ROOT whose children are
    /// all one floor: <c>GetComponentInParent</c> then answers for a stair's
    /// tread trigger and its hidden ramp alike, and a rebuilt cemetery slab
    /// that swaps its mesh keeps the marker it had.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FootstepGround : MonoBehaviour
    {
        [SerializeField] private FootstepGroundKind kind;

        public FootstepGroundKind Kind => kind;

        /// <summary>
        /// Get-or-add. A null target returns null, so a builder can pass
        /// through a result it may not have built.
        /// </summary>
        public static FootstepGround Stamp(
            GameObject target,
            FootstepGroundKind kind)
        {
            if (target == null)
            {
                return null;
            }

            FootstepGround marker = target.GetComponent<FootstepGround>();
            if (marker == null)
            {
                marker = target.AddComponent<FootstepGround>();
            }

            marker.kind = kind;
            return marker;
        }

        public static FootstepGround Stamp(
            Component target,
            FootstepGroundKind kind)
        {
            return target == null ? null : Stamp(target.gameObject, kind);
        }
    }

    /// <summary>
    /// A floor with no collider of its own: a rug on the boards, the tiles
    /// laid over the home's plank floor, the puddle film on the road. A
    /// downward ray finds the collider underneath and would name the wrong
    /// floor, so an overlay is asked FIRST, by footprint, and wins when the
    /// feet stand on it.
    ///
    /// Registration is explicit in <see cref="Initialize"/> and the list
    /// prunes destroyed entries as it walks: EditMode raises neither
    /// <c>OnEnable</c> nor <c>OnDisable</c> for a plain component, and a
    /// scene that unloads takes its overlays with it either way.
    /// </summary>
    public class FootstepGroundOverlay : MonoBehaviour
    {
        /// <summary>
        /// How far above or below an overlay's face the feet may stand and
        /// still be on it - a step's worth, so a flight above a rug never
        /// reads as the rug.
        /// </summary>
        public const float MaximumContactOffset = 0.4f;

        private static readonly List<FootstepGroundOverlay> Overlays =
            new List<FootstepGroundOverlay>();

        private readonly List<RuntimeOrientedBox> footprint =
            new List<RuntimeOrientedBox>();

        public FootstepGroundKind Kind { get; private set; }
        public IReadOnlyList<RuntimeOrientedBox> Footprint => footprint;
        internal static int RegisteredCount => Overlays.Count;

        public void Initialize(
            FootstepGroundKind kind,
            IReadOnlyList<RuntimeOrientedBox> boxes)
        {
            if (kind == FootstepGroundKind.None)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            if (boxes == null)
            {
                throw new ArgumentNullException(nameof(boxes));
            }

            Kind = kind;
            footprint.Clear();
            footprint.AddRange(boxes);
            if (!Overlays.Contains(this))
            {
                Overlays.Add(this);
            }
        }

        /// <summary>
        /// An overlay for a render-only runtime primitive box - a rug, a
        /// tiled floor laid over the boards - whose footprint is the box's
        /// own transform: a unit cube scaled by its size.
        /// </summary>
        public static FootstepGroundOverlay AddForPrimitiveBox(
            GameObject box,
            FootstepGroundKind kind)
        {
            if (box == null)
            {
                return null;
            }

            Transform transform = box.transform;
            var overlay = box.AddComponent<FootstepGroundOverlay>();
            overlay.Initialize(
                kind,
                new[]
                {
                    new RuntimeOrientedBox(
                        transform.position,
                        transform.rotation,
                        transform.lossyScale)
                });
            return overlay;
        }

        /// <summary>Whether feet at this position stand on the overlay.</summary>
        public virtual bool IsActiveAt(Vector3 feet)
        {
            for (int index = 0; index < footprint.Count; index++)
            {
                if (Contains(footprint[index], feet))
                {
                    return true;
                }
            }

            return false;
        }

        protected static bool Contains(RuntimeOrientedBox box, Vector3 feet)
        {
            return box.TrySampleTop(feet, out float topY) &&
                   Mathf.Abs(feet.y - topY) <= MaximumContactOffset;
        }

        /// <summary>
        /// The kind of the first registered overlay the feet stand on.
        /// Later registrations are asked first, so a rug laid after the
        /// floor it lies on wins.
        /// </summary>
        public static bool TryFind(Vector3 feet, out FootstepGroundKind kind)
        {
            for (int index = Overlays.Count - 1; index >= 0; index--)
            {
                FootstepGroundOverlay overlay = Overlays[index];
                if (overlay == null)
                {
                    Overlays.RemoveAt(index);
                    continue;
                }

                if (!overlay.enabled ||
                    !overlay.gameObject.activeInHierarchy ||
                    !overlay.IsActiveAt(feet))
                {
                    continue;
                }

                kind = overlay.Kind;
                return true;
            }

            kind = FootstepGroundKind.None;
            return false;
        }

        private void OnDisable()
        {
            Overlays.Remove(this);
        }

        private void OnDestroy()
        {
            Overlays.Remove(this);
        }

        internal static void ResetForTests()
        {
            Overlays.Clear();
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            Overlays.Clear();
        }
    }

    /// <summary>
    /// The gutter puddles: an overlay that is only there while the film is.
    ///
    /// Mirrors the water shader's puddle film - the rim mask erodes from
    /// the edge inward against the square root of the wetness - with the
    /// edge noise at its mean, so a boot splashes where the eye sees water
    /// and knocks on the road where the puddle has dried back to its middle.
    /// </summary>
    public sealed class PuddleFootstepOverlay : FootstepGroundOverlay
    {
        /// <summary>The shader's `0.4 + 0.6 * noise`, averaged.</summary>
        public const float MeanEdgeNoise = 0.7f;

        /// <summary>
        /// Half of the shader's `smoothstep(0, 0.12)`: the film is at least
        /// half opaque, which is where a puddle reads as water.
        /// </summary>
        public const float FilmThreshold = 0.06f;

        public void Initialize(IReadOnlyList<RuntimeOrientedBox> patches)
        {
            Initialize(FootstepGroundKind.Puddle, patches);
        }

        /// <summary>
        /// Whether the film covers this point of a patch at this wetness.
        /// Pure, so the mirror of the shader can be pinned by a test.
        /// </summary>
        public static bool IsWetAt(
            RuntimeOrientedBox patch,
            Vector3 feet,
            float wetness,
            float edgeBite)
        {
            if (!patch.TrySampleTop(feet, out float topY) ||
                Mathf.Abs(feet.y - topY) > MaximumContactOffset)
            {
                return false;
            }

            // The rim mask is 1 at the patch centre and 0 on its rim,
            // interpolated bilinearly over a 3x3 grid: the product of the
            // two axis pyramids.
            Vector3 local = Quaternion.Inverse(patch.Rotation) *
                            (new Vector3(feet.x, topY, feet.z) -
                             patch.Center);
            float u = Mathf.Abs(local.x) /
                      Mathf.Max(0.0001f, patch.Size.x * 0.5f);
            float v = Mathf.Abs(local.z) /
                      Mathf.Max(0.0001f, patch.Size.z * 0.5f);
            float mask = Mathf.Clamp01(1f - u) * Mathf.Clamp01(1f - v);
            float erosion =
                (1f - mask) * MeanEdgeNoise * Mathf.Max(0.05f, edgeBite);
            float shore = Mathf.Sqrt(Mathf.Clamp01(wetness));
            return shore - erosion >= FilmThreshold;
        }

        public override bool IsActiveAt(Vector3 feet)
        {
            float wetness = CityWetSurfaceRegistry.CurrentWetness;
            float edgeBite = CityPuddleWaterResources.EdgeBite;
            IReadOnlyList<RuntimeOrientedBox> patches = Footprint;
            for (int index = 0; index < patches.Count; index++)
            {
                if (IsWetAt(patches[index], feet, wetness, edgeBite))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
