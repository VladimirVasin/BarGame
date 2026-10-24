using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Where each letter of a line has drifted to. The whole geometry of the
    /// spoken words coming apart at the top of the intoxication scale, kept
    /// pure so it can be proved without a game view — the same reason
    /// <see cref="NpcSpeechBubbleView.ResolvePanelRect"/> is a static.
    ///
    /// A glyph is addressed BY INDEX rather than by walking a random stream, so
    /// the drawing order cannot change what a letter does. The three channels
    /// (sideways, downward, rotation) are three salted hashes of the line's own
    /// seed, which is the mechanism the letter blips already use for their
    /// pitch; nothing here allocates and nothing here remembers.
    ///
    /// Two invariants carry the reading. A letter starts EXACTLY where it was
    /// typed — the amplitude ramps in from zero over <see
    /// cref="RampSeconds"/> — so the line assembles legibly and only then falls
    /// apart. Glyphs align to whole pixels on the shared logical canvas,
    /// keeping their edges sharp while the scattered line moves.
    ///
    /// The view borrows wrapped glyph origins from Unity's ordinary label
    /// layout; this geometry moves each glyph relative to its own row.
    /// </summary>
    public static class SpeechScatterLayout
    {
        /// <summary>How far sideways and up a letter can wander at full
        /// scatter. Against a nine-pixel line and a five-pixel advance, seven
        /// pixels is more than a whole character cell: word shape is gone.
        /// </summary>
        public const float MaximumDriftPixels = 7f;

        /// <summary>The letters also settle downward, which is what makes this
        /// read as falling apart rather than as shivering.</summary>
        public const float SagPixels = 3f;

        public const float MaximumRotationDegrees = 18f;
        public const float MinimumRateRadians = 0.7f;
        public const float MaximumRateRadians = 2.3f;

        /// <summary>A letter holds its typed position for this long, so a line
        /// is read before it is lost.</summary>
        public const float RampSeconds = 0.35f;

        /// <summary>Below this the ordinary single-label path draws the line,
        /// preserving the ordinary layout whenever no perception/local slur applies.
        /// </summary>
        public const float ScatterEpsilon = 0.0005f;

        /// <summary>A rotated glyph needs room in its own rect or the style's
        /// clipping shaves its corners.</summary>
        public const float GlyphPaddingPixels = 2f;

        /// <summary>IMGUI does not clip to the retro canvas, so a glyph that
        /// drifted out of it would draw in the letterbox.</summary>
        public const float CanvasMarginPixels = 2f;

        private const uint DriftXSalt = 0x44524658u;   // "DRFX"
        private const uint DriftYSalt = 0x44524659u;   // "DRFY"
        private const uint RotationSalt = 0x524F5441u; // "ROTA"
        private const uint RateSalt = 0x52415445u;     // "RATE"

        public static bool IsScattering(float amount)
        {
            return !float.IsNaN(amount) && amount > ScatterEpsilon;
        }

        /// <summary>
        /// Rotate in logical canvas space before its scale and letterbox offset.
        /// GUIUtility.RotateAroundPivot rotates after the canvas transform and
        /// treats this logical pivot as a screen point, making glyphs orbit it.
        /// </summary>
        public static Matrix4x4 ResolveGlyphMatrix(in Matrix4x4 canvasMatrix, Vector2 pivot, float degrees)
        {
            if (degrees == 0f) return canvasMatrix;
            var center = new Vector3(pivot.x, pivot.y, 0f);
            return canvasMatrix * Matrix4x4.TRS(center, Quaternion.Euler(0f, 0f, degrees), Vector3.one) *
                   Matrix4x4.Translate(-center);
        }

        /// <summary>
        /// Cumulative advance of the glyph at <paramref name="glyphIndex"/>
        /// from the start of the row. Taken from PREFIX widths of the whole
        /// string rather than from per-character measurements, so kerning is
        /// the font's own and the row a glyph walks matches the row the single
        /// label would have drawn.
        /// </summary>
        public static float ResolvePenX(
            float[] prefixWidths,
            int glyphIndex)
        {
            if (prefixWidths == null || prefixWidths.Length == 0)
            {
                return 0f;
            }

            int index = Mathf.Clamp(
                glyphIndex,
                0,
                prefixWidths.Length - 1);
            return prefixWidths[index];
        }

        public static float ResolveGlyphWidth(
            float[] prefixWidths,
            int glyphIndex)
        {
            if (prefixWidths == null || glyphIndex < 0 ||
                glyphIndex + 1 >= prefixWidths.Length)
            {
                return 0f;
            }

            return Mathf.Max(
                0f,
                prefixWidths[glyphIndex + 1] - prefixWidths[glyphIndex]);
        }

        /// <summary>
        /// How far this letter has wandered, and how far it has tipped over.
        /// Zero at <paramref name="elapsedSeconds"/> zero and zero at
        /// <paramref name="amount"/> zero, both exactly.
        /// </summary>
        public static void ResolveGlyph(
            int glyphIndex,
            uint seed,
            float elapsedSeconds,
            float amount,
            out Vector2 offsetPixels,
            out float rotationDegrees)
        {
            offsetPixels = Vector2.zero;
            rotationDegrees = 0f;
            if (!IsScattering(amount) ||
                float.IsNaN(elapsedSeconds) ||
                elapsedSeconds <= 0f)
            {
                return;
            }

            uint index = (uint)Mathf.Max(0, glyphIndex);
            uint glyphSeed = CitySoundStableHash.Combine(seed, index);
            float reach = Mathf.Clamp01(amount) *
                          Mathf.Clamp01(elapsedSeconds / RampSeconds);

            float driftX = Wave(glyphSeed, DriftXSalt, elapsedSeconds);
            float driftY = Wave(glyphSeed, DriftYSalt, elapsedSeconds);
            float turn = Wave(glyphSeed, RotationSalt, elapsedSeconds);

            offsetPixels = new Vector2(
                reach * MaximumDriftPixels * driftX,
                reach * (MaximumDriftPixels * driftY + SagPixels));
            rotationDegrees = reach * MaximumRotationDegrees * turn;
        }

        /// <summary>
        /// The rect one glyph is drawn in: its own place on the row, moved by
        /// its drift, padded for rotation, kept inside the canvas and snapped
        /// to whole pixels.
        /// </summary>
        public static Rect ResolveGlyphRect(
            Vector2 rowOrigin,
            float penX,
            float glyphWidth,
            float lineHeight,
            Vector2 offsetPixels)
        {
            float width = Mathf.Max(1f, glyphWidth) +
                          GlyphPaddingPixels * 2f;
            float height = Mathf.Max(1f, lineHeight) +
                           GlyphPaddingPixels * 2f;
            var rect = new Rect(
                rowOrigin.x + penX + offsetPixels.x - GlyphPaddingPixels,
                rowOrigin.y + offsetPixels.y - GlyphPaddingPixels,
                width,
                height);
            rect.x = Mathf.Clamp(
                rect.x,
                CanvasMarginPixels,
                Mathf.Max(
                    CanvasMarginPixels,
                    RetroUiTheme.LogicalWidth - CanvasMarginPixels - width));
            rect.y = Mathf.Clamp(
                rect.y,
                CanvasMarginPixels,
                Mathf.Max(
                    CanvasMarginPixels,
                    RetroUiTheme.LogicalHeight -
                    CanvasMarginPixels -
                    height));
            return RetroUiTheme.SnapRect(rect);
        }

        /// <summary>
        /// One channel: a sine on its own rate and its own phase, both drawn
        /// from the glyph's hash so no two letters move together.
        /// </summary>
        private static float Wave(
            uint glyphSeed,
            uint channelSalt,
            float elapsedSeconds)
        {
            uint channel = CitySoundStableHash.Combine(
                glyphSeed,
                channelSalt);
            float phase = CitySoundStableHash.ToUnitFloat(channel) *
                          Mathf.PI *
                          2f;
            float rate = Mathf.Lerp(
                MinimumRateRadians,
                MaximumRateRadians,
                CitySoundStableHash.ToUnitFloat(
                    CitySoundStableHash.Combine(channel, RateSalt)));
            return Mathf.Sin(elapsedSeconds * rate + phase);
        }
    }
}
