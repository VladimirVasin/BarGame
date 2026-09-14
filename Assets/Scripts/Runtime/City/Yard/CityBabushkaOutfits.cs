using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Four outfits for the one grandmother model the city places four
    /// times: three at the drying-yard beating frame and one standing
    /// over the upturned bicycle in a residential pocket. The model is
    /// deliberately shared - her `BabushkaBeat` and `BabushkaSmoke` are
    /// what make those sites worth staging - but four bodies in the same
    /// dark dress read as one person copied, and the palette variant is
    /// a multiplier within a tenth of one, so it cannot separate them.
    ///
    /// Each outfit names only the garment palettes the model declares
    /// (`gran_*`), so skin, boots, soles and eyes stay the design's own.
    /// The bright note in the drying yard belongs to the plastic carpet
    /// beaters, which is why every scarf here stays a muted, worn tone.
    /// </summary>
    public static class CityBabushkaOutfits
    {
        /// <summary>Rusty red scarf over a dark plum robe: the model's
        /// authored colours, kept for the southern beater so the design
        /// still shows the dress it was painted in.</summary>
        private static readonly CityPedestrianOutfit RustAndPlum =
            Create(
                new Color(0.400f, 0.135f, 0.085f),
                new Color(0.210f, 0.070f, 0.048f),
                new Color(0.150f, 0.090f, 0.125f),
                new Color(0.075f, 0.045f, 0.065f),
                new Color(0.085f, 0.060f, 0.085f),
                new Color(0.320f, 0.300f, 0.250f),
                new Color(0.100f, 0.095f, 0.090f));

        /// <summary>Faded teal scarf, brown robe, grey-blue apron.</summary>
        private static readonly CityPedestrianOutfit TealAndBrown =
            Create(
                new Color(0.115f, 0.205f, 0.195f),
                new Color(0.055f, 0.105f, 0.100f),
                new Color(0.155f, 0.105f, 0.065f),
                new Color(0.080f, 0.055f, 0.035f),
                new Color(0.075f, 0.070f, 0.055f),
                new Color(0.225f, 0.245f, 0.260f),
                new Color(0.130f, 0.120f, 0.100f));

        /// <summary>Mustard scarf, olive robe, a washed-out apron.</summary>
        private static readonly CityPedestrianOutfit OchreAndOlive =
            Create(
                new Color(0.345f, 0.250f, 0.090f),
                new Color(0.175f, 0.125f, 0.045f),
                new Color(0.115f, 0.125f, 0.080f),
                new Color(0.060f, 0.065f, 0.040f),
                new Color(0.070f, 0.075f, 0.060f),
                new Color(0.360f, 0.350f, 0.315f),
                new Color(0.090f, 0.085f, 0.090f));

        /// <summary>Dark green scarf, ash robe, brown apron.</summary>
        private static readonly CityPedestrianOutfit GreenAndAsh =
            Create(
                new Color(0.120f, 0.165f, 0.095f),
                new Color(0.060f, 0.085f, 0.050f),
                new Color(0.135f, 0.135f, 0.140f),
                new Color(0.070f, 0.070f, 0.075f),
                new Color(0.085f, 0.085f, 0.080f),
                new Color(0.280f, 0.225f, 0.170f),
                new Color(0.105f, 0.100f, 0.095f));

        private static readonly CityPedestrianOutfit[] YardSlots =
        {
            RustAndPlum,
            TealAndBrown,
            OchreAndOlive
        };

        /// <summary>The fourth woman, a courtyard away from the other
        /// three and never in frame with them, still gets her own.</summary>
        public static CityPedestrianOutfit CourtyardBicycle => GreenAndAsh;

        /// <summary>
        /// The drying yard's three, by the slot the plan gives each: the
        /// southern beater, the northern beater and the smoker.
        /// </summary>
        public static CityPedestrianOutfit ForYardSlot(int slot)
        {
            int index = slot % YardSlots.Length;
            return YardSlots[index < 0 ? index + YardSlots.Length : index];
        }

        private static CityPedestrianOutfit Create(
            Color scarf,
            Color scarfShadow,
            Color robe,
            Color robeShadow,
            Color skirt,
            Color apron,
            Color wool)
        {
            return new CityPedestrianOutfit(
                new CityPedestrianOutfitGarment("gran_scarf", scarf),
                new CityPedestrianOutfitGarment("gran_scarf_dark", scarfShadow),
                new CityPedestrianOutfitGarment("gran_robe", robe),
                new CityPedestrianOutfitGarment("gran_robe_dark", robeShadow),
                new CityPedestrianOutfitGarment("gran_skirt", skirt),
                new CityPedestrianOutfitGarment("gran_apron", apron),
                new CityPedestrianOutfitGarment("gran_wool", wool));
        }
    }
}
