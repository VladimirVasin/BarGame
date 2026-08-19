using TMPro;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The three lines standing on the brass, and what keeps them in
    /// step with what the plaque says.
    ///
    /// It exists because the board is fitted before the hero has
    /// written anything — the camera walks round to the front of the
    /// stone and a bare face there would be asking him to inscribe
    /// nothing — so the last line has to be set again the moment it is
    /// cut.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CemeteryPlaqueSurface : MonoBehaviour
    {
        /// <summary>Point sizes on a board `0.30 m` across. TMP works
        /// in its own units, so these are set against the plate's own
        /// rect rather than against metres.</summary>
        public const float NameSize = 3.4f;
        public const float YearsSize = 2.2f;
        public const float EpitaphSize = 2.4f;

        /// <summary>
        /// The epitaph is the one line whose length is not known when
        /// the board is built, so it is the one line allowed to shrink
        /// itself to fit rather than run off the brass.
        /// </summary>
        public const float EpitaphMinimumSize = 1.3f;

        private TMP_Text nameLine;
        private TMP_Text yearsLine;
        private TMP_Text epitaphLine;

        /// <summary>The hero's own line as it stands on the board.
        /// </summary>
        public string EpitaphText =>
            epitaphLine != null ? epitaphLine.text : string.Empty;

        internal void Bind(
            TMP_Text name,
            TMP_Text years,
            TMP_Text epitaph)
        {
            nameLine = name;
            yearsLine = years;
            epitaphLine = epitaph;
        }

        /// <summary>
        /// Shows a line that is still being typed, with a cut mark at
        /// the end of it so the hero can see where he is. This is the
        /// entire text-entry interface: the board fills up in front of
        /// him and there is no field anywhere.
        /// </summary>
        public void ShowDraft(string draft)
        {
            if (epitaphLine == null)
            {
                return;
            }

            epitaphLine.text = string.IsNullOrEmpty(draft)
                ? "_"
                : draft + "_";
        }

        /// <summary>
        /// Sets the three lines from whatever the session currently
        /// holds. The first two are what nobody could tell him; the
        /// third falls back to a stated absence, because a blank line
        /// would read as a bug and an unwritten plaque is a real
        /// outcome.
        /// </summary>
        public void Refresh()
        {
            if (nameLine != null)
            {
                nameLine.text = LocalizationService.Get(
                    CemeteryEpitaph.UnknownNameKey);
            }

            if (yearsLine != null)
            {
                yearsLine.text = LocalizationService.Get(
                    CemeteryEpitaph.UnknownYearsKey);
            }

            if (epitaphLine == null)
            {
                return;
            }

            string written = GameSessionState.GraveEpitaph;
            epitaphLine.text = string.IsNullOrEmpty(written)
                ? LocalizationService.Get(
                    CemeteryGraveWorkView.EmptyEpitaphKey)
                : written;
        }
    }
}
