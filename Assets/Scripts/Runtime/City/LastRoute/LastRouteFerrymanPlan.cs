using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Where the Ferryman waits and which way he looks. Both come off the
    /// car that was actually placed, never off the layout: if the seed
    /// leaves nowhere to park without blocking a way in, there is no car,
    /// and a man perched on a car that is not there is worse than no man.
    /// </summary>
    public readonly struct LastRouteFerrymanStance
    {
        public LastRouteFerrymanStance(
            Vector3 position,
            Vector3 facing,
            int paletteVariant,
            float playbackSpeed,
            float phaseOffsetSeconds)
        {
            Position = position;
            Facing = facing;
            PaletteVariant = paletteVariant;
            PlaybackSpeed = playbackSpeed;
            PhaseOffsetSeconds = phaseOffsetSeconds;
        }

        /// <summary>His soles, which is also his root: the model is drawn
        /// with its lowest sole on z = 0 and the perch validator confirms
        /// that a boot is what touches down.</summary>
        public Vector3 Position { get; }
        public Vector3 Facing { get; }
        public int PaletteVariant { get; }
        public float PlaybackSpeed { get; }
        public float PhaseOffsetSeconds { get; }
    }

    /// <summary>
    /// The one man at the last route island: sitting on the bonnet of his
    /// own car with his boots on the bumper, throwing a coin, facing out
    /// over the nose at whoever is walking up. Absent whenever the car is.
    /// </summary>
    public sealed class LastRouteFerrymanPlan
    {
        /// <summary>
        /// He does not breathe in step with the fisherman. A shared phase
        /// across two authored idles reads as one animation played twice,
        /// so his loop starts a second and a half in.
        /// </summary>
        public const float PhaseOffsetSeconds = 1.5f;

        private static readonly LastRouteFerrymanPlan AbsentPlan =
            new LastRouteFerrymanPlan(default, false);

        private LastRouteFerrymanPlan(
            LastRouteFerrymanStance stance,
            bool isPresent)
        {
            Stance = stance;
            IsPresent = isPresent;
        }

        public LastRouteFerrymanStance Stance { get; }
        public bool IsPresent { get; }

        public static LastRouteFerrymanPlan Create(
            LastRouteCarAssetRegistry car)
        {
            if (car == null ||
                car.PerchSolesAnchor == null ||
                car.PerchSeatAnchor == null)
            {
                return AbsentPlan;
            }

            // Out over the nose, taken as the vector from the bonnet he
            // sits on to the bumper his boots rest on. Deliberately NOT
            // read off an anchor's own forward: these are nodes of an
            // imported FBX, and this project has now been bitten five
            // times by assuming an imported node shares its object's axes.
            // The difference between two drawn points has no basis to be
            // wrong about.
            Vector3 facing = car.PerchSolesAnchor.position -
                             car.PerchSeatAnchor.position;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.000001f)
            {
                return AbsentPlan;
            }

            return new LastRouteFerrymanPlan(
                new LastRouteFerrymanStance(
                    car.PerchSolesAnchor.position,
                    facing.normalized,
                    0,
                    1f,
                    PhaseOffsetSeconds),
                true);
        }
    }
}
