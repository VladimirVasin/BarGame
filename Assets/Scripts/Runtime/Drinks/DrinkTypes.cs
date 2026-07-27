namespace BarPromenade
{
    public enum DrinkId
    {
        None = 0,
        LightBeer,
        DarkBeer,
        WhiteWine,
        RedWine,
        Vodka,
        PepperVodka,
        CognacVs,
        CognacVsop,
        Water
    }

    public enum DrinkFamily
    {
        None = 0,
        Beer,
        Wine,
        Vodka,
        Cognac,
        Water
    }

    public readonly struct DrinkDefinition
    {
        public DrinkDefinition(
            DrinkId id,
            DrinkFamily family,
            int intoxicationGain)
        {
            Id = id;
            Family = family;
            IntoxicationGain = intoxicationGain;
        }

        public DrinkId Id { get; }
        public DrinkFamily Family { get; }
        public int IntoxicationGain { get; }
    }

}
