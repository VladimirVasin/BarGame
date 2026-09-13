using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Names the floor under the hero's feet and plays its step.
    ///
    /// The motor decides WHEN a step happens; a scene's claimant
    /// (<see cref="IPlayerFootstepSurface"/>) is asked first because it owns
    /// sound and effect together; this is what answers next, for every
    /// scene, before the motor falls back to the plain footstep. Overlays
    /// are asked before the ray, because a rug has no collider and the ray
    /// would find the boards under it.
    /// </summary>
    public static class HeroFootstepGround
    {
        public const float ProbeStartHeight = 0.6f;
        public const float ProbeDistance = 1.2f;

        private static readonly RaycastHit[] Hits = new RaycastHit[32];

        public static RetroSfxId ToSfx(FootstepGroundKind kind)
        {
            switch (kind)
            {
                case FootstepGroundKind.Concrete:
                    return RetroSfxId.FootstepConcrete;
                case FootstepGroundKind.Stone:
                    return RetroSfxId.FootstepStone;
                case FootstepGroundKind.Grass:
                    return RetroSfxId.FootstepGrass;
                case FootstepGroundKind.Soil:
                    return RetroSfxId.FootstepSoil;
                case FootstepGroundKind.Sand:
                    return RetroSfxId.FootstepSand;
                case FootstepGroundKind.Snow:
                    return RetroSfxId.FootstepSnow;
                case FootstepGroundKind.Wood:
                    return RetroSfxId.FootstepWood;
                case FootstepGroundKind.Carpet:
                    return RetroSfxId.FootstepCarpet;
                case FootstepGroundKind.Tile:
                    return RetroSfxId.FootstepTile;
                case FootstepGroundKind.Puddle:
                    return RetroSfxId.FootstepPuddle;
                default:
                    return RetroSfxId.None;
            }
        }

        /// <summary>
        /// The floor under <paramref name="feet"/>: an overlay the feet
        /// stand on, else the stamped kind of the topmost real ground a ray
        /// finds. False for ground nobody stamped, which keeps the plain
        /// footstep.
        /// </summary>
        public static bool TryResolve(
            Vector3 feet,
            Transform hero,
            out FootstepGroundKind kind,
            out Vector3 contact)
        {
            contact = feet;
            if (FootstepGroundOverlay.TryFind(feet, out kind))
            {
                return true;
            }

            kind = FootstepGroundKind.None;
            int count = Physics.RaycastNonAlloc(
                feet + Vector3.up * ProbeStartHeight,
                Vector3.down,
                Hits,
                ProbeDistance,
                FootProbeSurface.ProbeMask,
                QueryTriggerInteraction.Collide);
            // A full buffer cannot establish which surface is topmost.
            if (count >= Hits.Length)
            {
                return false;
            }

            float closest = float.PositiveInfinity;
            Collider support = null;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = Hits[index];
                Collider collider = hit.collider;
                // Triggers are asked for so a stair's tread collider
                // counts as the floor; any other trigger - a door volume,
                // the hero's own cloth - is not a floor.
                if (collider == null ||
                    (collider.isTrigger &&
                     !FootProbeSurface.IsProbeSurface(collider)) ||
                    (hero != null &&
                     collider.transform.IsChildOf(hero)) ||
                    hit.normal.y <= 0.001f ||
                    hit.distance >= closest)
                {
                    continue;
                }

                closest = hit.distance;
                support = collider;
                contact = hit.point;
            }

            if (support == null)
            {
                contact = feet;
                return false;
            }

            FootstepGround marker =
                support.GetComponentInParent<FootstepGround>();
            if (marker == null || marker.Kind == FootstepGroundKind.None)
            {
                contact = feet;
                return false;
            }

            kind = marker.Kind;
            return true;
        }

        /// <summary>
        /// Plays the step of the floor under the feet. False when no floor
        /// is known there, so the caller plays the plain footstep.
        /// </summary>
        public static bool TryPlay(Vector3 feet, Transform hero)
        {
            if (!TryResolve(
                    feet,
                    hero,
                    out FootstepGroundKind kind,
                    out Vector3 contact))
            {
                return false;
            }

            RetroAudio.PlayAt(ToSfx(kind), contact);
            return true;
        }
    }
}
