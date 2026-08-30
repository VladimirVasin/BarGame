using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Where one raven stands and which way it faces. The
    /// position sits at the cemetery's ground top (or the mound's
    /// crown), and the yaw is a plain compass heading the actor turns
    /// into a rotation.</summary>
    public readonly struct CemeteryRavenPerch
    {
        public CemeteryRavenPerch(
            bool isPresent,
            string plotId,
            Vector3 position,
            float yawDegrees)
        {
            IsPresent = isPresent;
            PlotId = plotId ?? string.Empty;
            Position = position;
            YawDegrees = yawDegrees;
        }

        public bool IsPresent { get; }

        /// <summary>The lattice plot the perch stands on: the sealed
        /// grave's own plot for raven A, the selected vacant plot for
        /// raven B.</summary>
        public string PlotId { get; }

        public Vector3 Position { get; }
        public float YawDegrees { get; }
    }

    /// <summary>
    /// Pure geometry and selection rules of the cemetery raven pair:
    /// where each bird sits and how the ground bird's plot is chosen.
    /// No scene state — everything here is EditMode-testable, the
    /// mourner plan's contract. Determinism across city rebuilds is
    /// structural: the inputs (cemetery plan, first-sealed plot id,
    /// ledger ids, open-job rest points) are identical on every
    /// rebuild, and the selection is an argmin over a candidate set
    /// that only ever shrinks, so a chosen plot stays chosen until
    /// something excludes it.
    /// </summary>
    public static class CemeteryRavenPlan
    {
        /// <summary>
        /// The preferred ring for the ground bird, measured from the
        /// mound crown: near enough to read as the pair it is, far
        /// enough that both never share one glance. The lattice pitch
        /// is 4.0 x 5.0 m, so orthogonal neighbours land inside the
        /// band by construction.
        /// </summary>
        public const float GroundPerchBandMinimumMeters = 3.5f;
        public const float GroundPerchBandMaximumMeters = 7.0f;

        /// <summary>Upper bound of the idle desync offset: anything
        /// past one preen interval buys nothing, and a dozen seconds
        /// already puts every cycle out of step.</summary>
        public const float MaximumIdleStartOffsetSeconds = 12f;

        /// <summary>Distances closer than this count as the same and
        /// fall through to the stable-id tie-break, so the choice
        /// never depends on float noise or list order.</summary>
        private const float TieEpsilonMeters = 0.0005f;

        /// <summary>
        /// Raven A's perch: the crown of the sealed grave's mound,
        /// facing down the plot toward the foot — silhouetted against
        /// the ground and alley, with the monument behind it rather
        /// than merged into it.
        /// </summary>
        public static CemeteryRavenPerch CreateMoundPerch(
            CemeteryGravediggingPlan gravePlan)
        {
            if (gravePlan == null)
            {
                throw new ArgumentNullException(nameof(gravePlan));
            }

            if (!gravePlan.IsPresent)
            {
                return default;
            }

            Vector3 crown =
                CityCemeterySealedGraveWorldBuilder
                    .GetMoundCrownPoint(gravePlan);
            Vector3 headward = gravePlan.Heading * Vector3.forward;
            float towardFootYaw =
                Mathf.Atan2(headward.x, headward.z) *
                Mathf.Rad2Deg + 180f;
            return new CemeteryRavenPerch(
                true,
                gravePlan.Plot.StableId,
                crown,
                towardFootYaw);
        }

        /// <summary>
        /// Raven B's perch: a vacant plot a few steps from the mound,
        /// facing the grave. Excluded outright: the grave's own plot,
        /// every plot the ledger has ever signed over (chalk marks
        /// and future worksites are no ground for a bird), and every
        /// vacant plot whose footprint carries an open job's coffin
        /// or spade rest point — those props legally lie PAST their
        /// own plot's edge, so the planner's clear-ground guarantee
        /// does not cover them. The caller collects the rest points
        /// of jobs between Marked and Filled, the span in which the
        /// props actually stand in the world.
        /// </summary>
        public static CemeteryRavenPerch SelectGroundPerch(
            CityCemeteryPlan cemeteryPlan,
            CemeteryGravediggingPlan gravePlan,
            ICollection<string> takenPlotIds,
            IReadOnlyList<Vector3> openJobRestPoints)
        {
            if (cemeteryPlan == null)
            {
                throw new ArgumentNullException(nameof(cemeteryPlan));
            }

            if (gravePlan == null)
            {
                throw new ArgumentNullException(nameof(gravePlan));
            }

            if (!gravePlan.IsPresent)
            {
                return default;
            }

            Vector3 crown =
                CityCemeterySealedGraveWorldBuilder
                    .GetMoundCrownPoint(gravePlan);
            var crownXZ = new Vector2(crown.x, crown.z);
            bool foundInBand = false;
            CityCemeteryPlotDescriptor bestInBand = default;
            float bestInBandDistance = float.PositiveInfinity;
            bool foundAny = false;
            CityCemeteryPlotDescriptor bestAny = default;
            float bestAnyDistance = float.PositiveInfinity;

            for (int index = 0;
                 index < cemeteryPlan.Plots.Count;
                 index++)
            {
                CityCemeteryPlotDescriptor plot =
                    cemeteryPlan.Plots[index];
                if (!plot.IsVacant ||
                    string.Equals(
                        plot.StableId,
                        gravePlan.Plot.StableId,
                        StringComparison.Ordinal) ||
                    (takenPlotIds != null &&
                     takenPlotIds.Contains(plot.StableId)) ||
                    CarriesAnyRestPoint(
                        plot.Footprint,
                        openJobRestPoints))
                {
                    continue;
                }

                float distance = Vector2.Distance(
                    new Vector2(plot.Ground.x, plot.Ground.z),
                    crownXZ);
                if (distance >= GroundPerchBandMinimumMeters &&
                    distance <= GroundPerchBandMaximumMeters)
                {
                    if (IsBetter(
                            distance,
                            plot,
                            foundInBand,
                            bestInBandDistance,
                            bestInBand))
                    {
                        foundInBand = true;
                        bestInBand = plot;
                        bestInBandDistance = distance;
                    }
                }

                if (IsBetter(
                        distance,
                        plot,
                        foundAny,
                        bestAnyDistance,
                        bestAny))
                {
                    foundAny = true;
                    bestAny = plot;
                    bestAnyDistance = distance;
                }
            }

            if (!foundAny)
            {
                return default;
            }

            // The band is a preference, never a veto: an unusually
            // crowded yard still seats the bird, just nearer or
            // further than it would like.
            CityCemeteryPlotDescriptor chosen =
                foundInBand ? bestInBand : bestAny;
            var position = new Vector3(
                chosen.Ground.x,
                cemeteryPlan.GroundTopY,
                chosen.Ground.z);
            return new CemeteryRavenPerch(
                true,
                chosen.StableId,
                position,
                ComputeYawToward(position, crown));
        }

        /// <summary>
        /// Per-raven seed: the city seed, the claimed grave and which
        /// of the two birds. Idle offsets, flight arcs and staggers
        /// all hang off this, so the pair differs everywhere while
        /// the shared trigger stays one value.
        /// </summary>
        public static int DeriveRavenSeed(
            int citySeed,
            string firstSealedPlotId,
            int ravenIndex)
        {
            if (ravenIndex != CemeteryRavenDirectorModel.RavenAIndex &&
                ravenIndex != CemeteryRavenDirectorModel.RavenBIndex)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ravenIndex));
            }

            unchecked
            {
                int salt = ravenIndex ==
                           CemeteryRavenDirectorModel.RavenAIndex
                    ? 0xA
                    : 0xB;
                return citySeed ^
                       (int)StableHash(firstSealedPlotId) ^
                       salt;
            }
        }

        /// <summary>Hashed start offset for one bird's idle timeline,
        /// 0 up to <see cref="MaximumIdleStartOffsetSeconds"/>.</summary>
        public static double DeriveIdleStartOffsetSeconds(
            int ravenSeed)
        {
            uint hash = Hash(unchecked((uint)ravenSeed ^ 0x1D1Eu));
            return (hash & 0x00FFFFFFu) / 16777215d *
                   MaximumIdleStartOffsetSeconds;
        }

        private static bool CarriesAnyRestPoint(
            Rect footprint,
            IReadOnlyList<Vector3> restPoints)
        {
            if (restPoints == null)
            {
                return false;
            }

            for (int index = 0; index < restPoints.Count; index++)
            {
                if (footprint.Contains(new Vector2(
                        restPoints[index].x,
                        restPoints[index].z)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The gravedigging register's own nearer-or-earlier
        /// rule: strictly nearer wins, and a tie falls to the ordinal
        /// smaller stable id so the choice never depends on list
        /// order.</summary>
        private static bool IsBetter(
            float distance,
            in CityCemeteryPlotDescriptor plot,
            bool found,
            float bestDistance,
            in CityCemeteryPlotDescriptor best)
        {
            bool nearer = !found ||
                          distance < bestDistance - TieEpsilonMeters;
            bool tiedAndEarlier =
                found &&
                !nearer &&
                distance <= bestDistance + TieEpsilonMeters &&
                string.CompareOrdinal(
                    plot.StableId,
                    best.StableId) < 0;
            return nearer || tiedAndEarlier;
        }

        private static float ComputeYawToward(
            Vector3 from,
            Vector3 to)
        {
            float dx = to.x - from.x;
            float dz = to.z - from.z;
            if (dx * dx + dz * dz < 0.000001f)
            {
                return 0f;
            }

            return Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// FNV-1a over the plot's stable id, restated from
        /// <see cref="CemeteryGravediggingPlan"/> where it is private:
        /// the id already encodes the lattice cell the city seed put
        /// the plot on, so it is the only per-grave entropy needed.
        /// </summary>
        private static uint StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string text = value ?? string.Empty;
                for (int index = 0; index < text.Length; index++)
                {
                    hash ^= text[index];
                    hash *= 16777619u;
                }

                return hash;
            }
        }

        private static uint Hash(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return value;
        }
    }
}
