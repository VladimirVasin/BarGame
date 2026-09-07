namespace BarPromenade
{
    /// <summary>
    /// Whether the Ferryman will drive the hero at all, by the story
    /// bible's §6 registry row of 2026-09-06.
    ///
    /// On the last two drunkenness stages — «Шатает» from `61` and
    /// «В стельку» from `81` — he does not. He says one line and stays on
    /// the bonnet, and that is the whole of it: nothing is taken from the
    /// player, the menu is not withdrawn, and the ride is his again as
    /// soon as the hero has come down off those stages. It is a man who
    /// will not put this one in his car tonight, not the city punishing
    /// anybody.
    ///
    /// Pure and level-only, deliberately: the trigger is the number, never
    /// what the hero says, does or sees, which is how §16.2 stays literally
    /// true — see <see cref="CityPedestrianInsultRules"/>, which reads the
    /// same number for the same reason.
    /// </summary>
    public static class LastRouteFerrymanRideRules
    {
        /// <summary>The first stage he refuses on: «Шатает», level `61`.
        /// Everything above it refuses too, so the last stage comes with
        /// it rather than being named twice.</summary>
        public const IntoxicationStage FirstRefusedStage =
            IntoxicationStage.Unsteady;

        /// <summary>The level that stage opens on, for readers and for
        /// logs; the rule below is stated in stages so it follows the
        /// scale if the scale ever moves.</summary>
        public const int FirstRefusedLevel = 61;

        public static bool RefusesToDrive(int intoxicationLevel)
        {
            return IntoxicationStageRules.GetStage(intoxicationLevel) >=
                   FirstRefusedStage;
        }
    }
}
