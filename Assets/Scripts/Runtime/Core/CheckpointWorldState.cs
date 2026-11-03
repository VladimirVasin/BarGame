using System.Collections.Generic;

namespace BarPromenade
{
    /// <summary>
    /// Scene-independent world data outside the session facade. Captures own
    /// their collections; restoring never hands a saved collection to live code.
    /// Unity objects and temporary presentation owners are deliberately excluded.
    /// </summary>
    internal sealed class CheckpointWorldState
    {
        private (bool Started, double Working, double Last) fishSupply;
        private KeyValuePair<string, int>[] potDocks;
        private LodgeStoveStage stove;
        private (bool Left, bool Right, bool Lantern) shelter;
        private (float Wetness, bool HasWeather, double LastMinutes) wetness;
        private HomeUrineResidue.CheckpointSnapshot deposits;
        private double coldExposure;
        private (CityPedestrianInsultWalk Walk, int Salt, bool Salted) streetSpeech;
        private LastRouteRideSpeechState roadSpeech;
        private MothersHouseMotherSpeechState motherSpeech;
        private LastRouteRadioMusicPlayer.CheckpointSnapshot radio;
        private ulong visitSerial;

        private CheckpointWorldState() { }

        internal static CheckpointWorldState Capture() => new CheckpointWorldState
        {
            fishSupply = CityFishSupplySession.CaptureCheckpoint(),
            potDocks = ChurchGardenPotSessionState.CaptureCheckpoint(),
            stove = LodgeStoveSessionState.CaptureCheckpoint(),
            shelter = LodgeShelterSessionState.CaptureCheckpoint(),
            wetness = CityWetSurfaceRegistry.CaptureCheckpoint(),
            deposits = HomeUrineResidue.CaptureCheckpoint(),
            coldExposure = AlpineColdExposure.CaptureCheckpoint(),
            streetSpeech = CityPedestrianInsultSessionState.CaptureCheckpoint(),
            roadSpeech = LastRouteRideSpeechSession.CaptureCheckpoint(),
            motherSpeech = MothersHouseMotherSpeechSession.CaptureCheckpoint(),
            radio = LastRouteRadioMusicPlayer.CaptureCheckpoint(),
            visitSerial = DefaultNpcPopulation.CaptureCheckpointVisits()
        };

        internal void Restore()
        {
            CityFishSupplySession.RestoreCheckpoint(fishSupply);
            ChurchGardenPotSessionState.RestoreCheckpoint(potDocks);
            LodgeStoveSessionState.RestoreCheckpoint(stove);
            LodgeShelterSessionState.RestoreCheckpoint(shelter);
            CityWetSurfaceRegistry.RestoreCheckpoint(wetness);
            HomeUrineResidue.RestoreCheckpoint(deposits);
            AlpineColdExposure.RestoreCheckpoint(coldExposure);
            CityPedestrianInsultSessionState.RestoreCheckpoint(streetSpeech);
            LastRouteRideSpeechSession.RestoreCheckpoint(roadSpeech);
            MothersHouseMotherSpeechSession.RestoreCheckpoint(motherSpeech);
            LastRouteRadioMusicPlayer.RestoreCheckpoint(radio);
            DefaultNpcPopulation.RestoreCheckpointVisits(visitSerial);
        }
    }
}
