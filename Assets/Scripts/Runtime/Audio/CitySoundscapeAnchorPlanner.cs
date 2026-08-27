using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Converts already-built city plans into causal sound descriptors. It
    /// deliberately has no fallback anchors: a missing physical object means
    /// silence, not an anonymous ambience source.
    /// </summary>
    public static class CitySoundscapeAnchorPlanner
    {
        public static CitySoundscapePlan Create(
            CityLayout layout,
            CityDecorationPlan decorationPlan)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (decorationPlan == null)
            {
                throw new ArgumentNullException(nameof(decorationPlan));
            }

            var sources = new List<CitySoundSourceDescriptor>();
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                AddPointOfInterest(
                    layout.DistrictPointsOfInterest[index],
                    sources);
            }

            AddFountains(layout, decorationPlan, sources);
            AddPlaygrounds(layout, decorationPlan, sources);
            return CitySoundscapePlanner.Create(layout.Seed, sources);
        }

        private static void AddPointOfInterest(
            CityDistrictPointOfInterestDescriptor point,
            ICollection<CitySoundSourceDescriptor> target)
        {
            if (point == null ||
                !CityDistrictPointOfInterestWorldBuilder
                    .TryDescribeSoundGeometry(
                        point,
                        out CityPointOfInterestSoundGeometry geometry))
            {
                return;
            }

            CitySoundPhysicalOwnerKind owner;
            CitySourceSoundId loopCue;
            CitySourceSoundId detailCue;
            CitySoundScheduleInterval detailSchedule;
            switch (point.Kind)
            {
                case CityDistrictPointOfInterestKind
                    .OldTownWaterworksCourt:
                    owner = CitySoundPhysicalOwnerKind
                        .OldTownWaterworksCourt;
                    loopCue = CitySourceSoundId.WaterworksPipeLoop;
                    detailCue = CitySourceSoundId.WaterworksDrip;
                    detailSchedule = new CitySoundScheduleInterval(9f, 21f);
                    break;
                case CityDistrictPointOfInterestKind
                    .ResidentialDryingYard:
                    owner = CitySoundPhysicalOwnerKind
                        .ResidentialDryingYard;
                    loopCue = CitySourceSoundId.DryingYardClothLoop;
                    detailCue = CitySourceSoundId.DryingYardRopeCreak;
                    detailSchedule = new CitySoundScheduleInterval(14f, 31f);
                    break;
                case CityDistrictPointOfInterestKind
                    .IndustrialWeighbridge:
                    owner = CitySoundPhysicalOwnerKind
                        .IndustrialWeighbridge;
                    loopCue = CitySourceSoundId
                        .IndustrialWeighbridgeMechanismLoop;
                    detailCue = CitySourceSoundId.IndustrialMetalStress;
                    // The plate may groan only when the real scale moves.
                    detailSchedule = CitySoundScheduleInterval.None;
                    break;
                case CityDistrictPointOfInterestKind
                    .NightlifeLastRouteIsland:
                    owner = CitySoundPhysicalOwnerKind
                        .NightlifeLastRouteIsland;
                    loopCue = CitySourceSoundId.LastRouteRelayLoop;
                    detailCue = CitySourceSoundId
                        .LastRouteIncompleteChime;
                    detailSchedule = new CitySoundScheduleInterval(32f, 67f);
                    break;
                default:
                    return;
            }

            CitySourceSoundDefinition loopDefinition =
                CitySourceSoundSynthesis.GetDefinition(loopCue);
            target.Add(new CitySoundSourceDescriptor(
                point.Id + ".sound.loop",
                point.District,
                owner,
                loopCue,
                geometry.LoopPosition,
                geometry.LoopOwnerBounds,
                loopDefinition.MaxDistance,
                CitySourceSoundPlayback.Loop,
                CitySoundScheduleInterval.None));

            CitySourceSoundDefinition detailDefinition =
                CitySourceSoundSynthesis.GetDefinition(detailCue);
            target.Add(new CitySoundSourceDescriptor(
                point.Id + ".sound.detail",
                point.District,
                owner,
                detailCue,
                geometry.DetailPosition,
                geometry.DetailOwnerBounds,
                detailDefinition.MaxDistance,
                CitySourceSoundPlayback.OneShot,
                detailSchedule));

            if (point.Kind ==
                CityDistrictPointOfInterestKind.ResidentialDryingYard)
            {
                CitySourceSoundDefinition strikeDefinition =
                    CitySourceSoundSynthesis.GetDefinition(
                        CitySourceSoundId.DryingYardCarpetStrike);
                target.Add(new CitySoundSourceDescriptor(
                    point.Id + ".sound.carpet-strike",
                    point.District,
                    owner,
                    CitySourceSoundId.DryingYardCarpetStrike,
                    geometry.DetailPosition,
                    geometry.DetailOwnerBounds,
                    strikeDefinition.MaxDistance,
                    CitySourceSoundPlayback.OneShot,
                    CitySoundScheduleInterval.None));
            }
        }

        private static void AddFountains(
            CityLayout layout,
            CityDecorationPlan decorationPlan,
            ICollection<CitySoundSourceDescriptor> target)
        {
            for (int index = 0;
                 index < decorationPlan.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor decoration =
                    decorationPlan.Descriptors[index];
                if (decoration.Kind !=
                    CityDecorationKind.ParkFountainAndStatue)
                {
                    continue;
                }

                CityDecorationWorldBuilder.GetDecorationFrame(
                    layout,
                    decoration,
                    out Vector3 origin,
                    out _,
                    out _);
                Vector3 position = origin +
                    Vector3.up * CityFountainWaterBuilder.BasinWaterTopY;
                float diameter =
                    CityFountainWaterBuilder.BasinWaterHalf * 2f;
                var ownerBounds = new Bounds(
                    position,
                    new Vector3(diameter, 0.24f, diameter));
                CitySourceSoundDefinition definition =
                    CitySourceSoundSynthesis.GetDefinition(
                        CitySourceSoundId.ParkFountainLoop);
                target.Add(new CitySoundSourceDescriptor(
                    decoration.StableId + ".sound.water",
                    CityDistrictKind.CentralPark,
                    CitySoundPhysicalOwnerKind.ParkFountainAndStatue,
                    CitySourceSoundId.ParkFountainLoop,
                    position,
                    ownerBounds,
                    definition.MaxDistance,
                    CitySourceSoundPlayback.Loop,
                    CitySoundScheduleInterval.None));
            }
        }

        private static void AddPlaygrounds(
            CityLayout layout,
            CityDecorationPlan decorationPlan,
            ICollection<CitySoundSourceDescriptor> target)
        {
            for (int index = 0;
                 index < decorationPlan.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor decoration =
                    decorationPlan.Descriptors[index];
                if (decoration.Kind !=
                    CityDecorationKind.ParkPlayground)
                {
                    continue;
                }

                CityDecorationWorldBuilder.GetDecorationFrame(
                    layout,
                    decoration,
                    out Vector3 origin,
                    out _,
                    out _);

                // The creak belongs to the beam, not to the plank: that
                // is where the rope is tied and where it takes the load
                // back. The seat's own position arrives at play time as
                // the override, so this only has to be the fixture's
                // honest resting anchor.
                Vector3 position = origin +
                    Vector3.up * CityPlaygroundGeometry.RopeAnchorY;

                // A Bounds is axis aligned and the frame is not, so the
                // envelope is squared off on the wider of the two spans.
                // It has to hold every point the seat can reach as well
                // as the beam, because the runtime plays from the seat.
                float span = Mathf.Max(
                    CityPlaygroundGeometry.TopBeamWidth,
                    CityPlaygroundGeometry.SeatReach * 2f);
                float top = CityPlaygroundGeometry.TopBeamY +
                    (CityPlaygroundGeometry.TopBeamThickness * 0.5f);
                var ownerBounds = new Bounds(
                    origin + Vector3.up * (top * 0.5f),
                    new Vector3(span, top, span));
                CitySourceSoundDefinition definition =
                    CitySourceSoundSynthesis.GetDefinition(
                        CitySourceSoundId.ParkSwingCreak);
                target.Add(new CitySoundSourceDescriptor(
                    decoration.StableId + ".sound.swing-creak",
                    CityDistrictKind.CentralPark,
                    CitySoundPhysicalOwnerKind.ParkPlayground,
                    CitySourceSoundId.ParkSwingCreak,
                    position,
                    ownerBounds,
                    definition.MaxDistance,
                    CitySourceSoundPlayback.OneShot,
                    CitySoundScheduleInterval.None));
            }
        }
    }
}
