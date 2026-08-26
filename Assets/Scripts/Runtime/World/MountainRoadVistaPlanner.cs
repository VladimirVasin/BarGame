using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Lays the view out in the cut's own frame: `u` along the corridor
    /// axis from the rim, `v` across it, `y` in the world.
    ///
    /// The distances are chosen against two limits at once. Nothing may
    /// sit past the `120 m` far plane, and nothing may sit near enough
    /// for the Exp2 fog to be the thing that describes it — at this
    /// area's `0.026` the fog is already `99 %` of the answer by `90 m`,
    /// which is exactly why these layers carry their own haze instead.
    /// Between those, `81` to `105 m` is the whole available stage, and
    /// the composition uses all of it - the far edge held short of the
    /// silhouette's own dissolve band so no layer is ever cut by it.
    ///
    /// Each layer is also only as wide as the cut actually shows at its
    /// own distance. There are deliberately no painted shoulders framing
    /// the gap: the walls of the cut are real ground, real snow and real
    /// sun, and a matte of them behind them was both hidden by them and
    /// worse than them.
    /// </summary>
    public static class MountainRoadVistaPlanner
    {
        public const float MistDistance = 81f;
        public const float ValleyDistance = 85f;
        public const float CityDistance = 92f;
        public const float HorizonDistance = 101f;

        /// <summary>
        /// The valley bed, one metre under the level of the tunnel mouth.
        /// </summary>
        public const float ValleyFloorY = -1f;

        private const int CityColumns = 17;
        private const int CityRows = 3;

        public static MountainRoadVistaPlan Create(
            MountainRoadPlateauDescriptor plateau,
            MountainRoadTerminalSitePlan site,
            int seed)
        {
            if (plateau == null)
            {
                throw new ArgumentNullException(nameof(plateau));
            }

            if (site == null)
            {
                throw new ArgumentNullException(nameof(site));
            }

            MountainRoadBrinkDescriptor brink = plateau.Brink ??
                throw new InvalidOperationException(
                    "A vista needs a brink to be seen over.");

            Vector3 axis = brink.Corridor.Axis;
            var lateral = new Vector3(axis.z, 0f, -axis.x);
            Vector3 eye = brink.Corridor.Apex;
            eye.y = site.TerraceTopY + 1.62f;

            var parts = new List<MountainRoadVistaPartDescriptor>(160);
            AppendMist(parts, eye, axis, lateral);
            AppendValley(parts, eye, axis, lateral, seed);
            AppendCity(parts, eye, axis, lateral, seed);
            AppendHorizon(parts, eye, axis, lateral, seed);

            return new MountainRoadVistaPlan(parts, eye, axis);
        }

        private static void AppendMist(
            ICollection<MountainRoadVistaPartDescriptor> parts,
            Vector3 eye,
            Vector3 axis,
            Vector3 lateral)
        {
            parts.Add(new MountainRoadVistaPartDescriptor(
                "vista-mist-band",
                MountainRoadVistaPartKind.MistBand,
                Place(eye, axis, lateral, MistDistance, 0f, 2.4f),
                new Vector3(34f, 2.6f, 3f),
                0f,
                0.94f));
        }

        /// <summary>
        /// The bed, and one pale thread switchbacking down it. That thread
        /// is the road: the last thing the view says is that you can see
        /// the way you came and it does not come back up.
        /// </summary>
        private static void AppendValley(
            ICollection<MountainRoadVistaPartDescriptor> parts,
            Vector3 eye,
            Vector3 axis,
            Vector3 lateral,
            int seed)
        {
            parts.Add(new MountainRoadVistaPartDescriptor(
                "vista-valley-floor",
                MountainRoadVistaPartKind.ValleyFloor,
                Place(
                    eye,
                    axis,
                    lateral,
                    ValleyDistance,
                    0f,
                    ValleyFloorY - 0.2f),
                new Vector3(38f, 0.4f, 14f),
                0f,
                0.5f));

            for (int index = 0; index < 6; index++)
            {
                float t = index / 5f;
                float lateralOffset = Mathf.Lerp(-11f, 9f, t) +
                                      (index % 2 == 0 ? 2.4f : -2.4f);
                parts.Add(new MountainRoadVistaPartDescriptor(
                    $"vista-valley-road-{index:00}",
                    MountainRoadVistaPartKind.ValleyFloor,
                    Place(
                        eye,
                        axis,
                        lateral,
                        ValleyDistance - 2.2f + Unit(seed, index, 0x52u),
                        lateralOffset,
                        ValleyFloorY + 0.06f),
                    new Vector3(6.4f, 0.12f, 0.5f),
                    index % 2 == 0 ? 26f : -26f,
                    0.78f));
            }
        }

        /// <summary>
        /// A grain, not a skyline. Seventeen columns three deep at this
        /// distance are a field of small marks; five tall towers would be
        /// a postcard, and this world does not print postcards.
        /// </summary>
        private static void AppendCity(
            ICollection<MountainRoadVistaPartDescriptor> parts,
            Vector3 eye,
            Vector3 axis,
            Vector3 lateral,
            int seed)
        {
            int index = 0;
            for (int row = 0; row < CityRows; row++)
            {
                for (int column = 0; column < CityColumns; column++)
                {
                    float jitter = Unit(seed, index, 0x43495459u);
                    float lateralOffset =
                        Mathf.Lerp(-13f, 13f, column / 16f) +
                        (jitter - 0.5f) * 1.1f;
                    float distance = CityDistance + row * 2.6f +
                                     (jitter - 0.5f) * 1.4f;
                    float height = 2.2f +
                                   Unit(seed, index, 0x48475432u) * 3.4f;
                    if (column == 8 && row == 1)
                    {
                        height = 7.6f;
                    }

                    float width = 0.85f +
                                  Unit(seed, index, 0x57445448u) * 1.35f;
                    parts.Add(new MountainRoadVistaPartDescriptor(
                        $"vista-city-block-{index:00}",
                        MountainRoadVistaPartKind.CityBlock,
                        Place(
                            eye,
                            axis,
                            lateral,
                            distance,
                            lateralOffset,
                            ValleyFloorY + height * 0.5f),
                        new Vector3(width, height, width * 0.9f),
                        (jitter - 0.5f) * 14f,
                        0.42f + row * 0.06f));

                    // One lit face per block, and only the near rows: a
                    // window this far away is a mark, and every mark on
                    // every building is a Christmas tree.
                    if (row < 2 &&
                        Unit(seed, index, 0x4C494748u) > 0.36f)
                    {
                        parts.Add(new MountainRoadVistaPartDescriptor(
                            $"vista-city-light-{index:00}",
                            MountainRoadVistaPartKind.LightPatch,
                            Place(
                                eye,
                                axis,
                                lateral,
                                distance - width * 0.5f,
                                lateralOffset,
                                ValleyFloorY + height * 0.62f),
                            new Vector3(
                                width * 0.72f,
                                height * 0.42f,
                                0.08f),
                            0f,
                            1f));
                    }

                    index++;
                }
            }
        }

        /// <summary>
        /// The far side of the valley, whose tops land within a few degrees
        /// of the standing eye. That is what puts the city BELOW the
        /// horizon instead of on it, and the whole drop depends on it.
        /// </summary>
        private static void AppendHorizon(
            ICollection<MountainRoadVistaPartDescriptor> parts,
            Vector3 eye,
            Vector3 axis,
            Vector3 lateral,
            int seed)
        {
            for (int index = 0; index < 7; index++)
            {
                float t = index / 6f;
                float height = 21f + Unit(seed, index, 0x484F5249u) * 11f;
                parts.Add(new MountainRoadVistaPartDescriptor(
                    $"vista-horizon-{index:00}",
                    MountainRoadVistaPartKind.HorizonRidge,
                    Place(
                        eye,
                        axis,
                        lateral,
                        HorizonDistance + (index % 2) * 1.8f,
                        Mathf.Lerp(-19f, 19f, t),
                        height * 0.5f),
                    new Vector3(
                        12f + Unit(seed, index, 0x57495448u) * 5f,
                        height,
                        6f),
                    (Unit(seed, index, 0x59415755u) - 0.5f) * 18f,
                    0.2f));
            }
        }

        private static Vector3 Place(
            Vector3 eye,
            Vector3 axis,
            Vector3 lateral,
            float distance,
            float lateralOffset,
            float worldY)
        {
            Vector3 point = eye +
                            axis * distance +
                            lateral * lateralOffset;
            point.y = worldY;
            return point;
        }

        /// <summary>The watchman's hash, so the matte is the seed's.</summary>
        private static float Unit(int seed, int index, uint salt)
        {
            unchecked
            {
                uint value = (uint)seed * 2654435761u;
                value ^= (uint)(index + 1) * 2246822519u;
                value ^= salt;
                value ^= value >> 15;
                value *= 2654435761u;
                value ^= value >> 13;
                return (value & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }
    }
}
