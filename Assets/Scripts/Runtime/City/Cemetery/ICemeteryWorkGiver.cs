namespace BarPromenade
{
    /// <summary>
    /// Whoever the watchman is speaking for when he offers work and
    /// when he settles up.
    ///
    /// It is an interface because the old man does not care how many
    /// graves are behind the offer. One job answers this, and so does
    /// the whole register of them; his window is the same window
    /// either way.
    /// </summary>
    public interface ICemeteryWorkGiver
    {
        /// <summary>True while there is a hole he can send a man to
        /// dig. A refusal does not spend it.</summary>
        bool CanOffer { get; }

        /// <summary>Hands the work over. False when there was nothing
        /// to hand over.</summary>
        bool TryAccept();

        /// <summary>
        /// Pays for every finished grave still owed for and answers
        /// the total. Zero when nothing is owed — a man with no
        /// closed grave behind him gets a word, not a wage.
        /// </summary>
        int CollectWages();
    }
}
