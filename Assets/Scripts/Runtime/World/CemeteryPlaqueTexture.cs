using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The words on the plaque, cut into a texture.
    ///
    /// The city's own <see cref="CitySignLettering"/> is eleven authored
    /// glyphs covering exactly what its signs spell, and a plaque has to
    /// carry whatever a player types. So this is a real font — five by
    /// seven pixels a glyph, Cyrillic and Latin and figures — rasterized
    /// into a small point-filtered texture that goes straight onto the
    /// plate. At that size it is not a compromise: a five-by-seven plate
    /// is exactly what a stamped municipal board looks like, and the
    /// renderer this game uses would make anything smoother look wrong.
    ///
    /// The lookup and the wrapping are pure and static so a test can
    /// prove that everything a player can type has a glyph, and that
    /// eight words always fit on the board.
    /// </summary>
    public static class CemeteryPlaqueTexture
    {
        public const int GlyphWidth = 5;
        public const int GlyphHeight = 7;

        /// <summary>Glyph plus the gap after it.</summary>
        public const int Advance = 6;

        /// <summary>
        /// Plate resolution. The board is `0.36 x 0.24 m`, and this is
        /// the same three-to-two, so a pixel is square on it.
        /// </summary>
        public const int TextureWidth = 168;
        public const int TextureHeight = 112;

        public const int Margin = 4;

        /// <summary>How many glyphs fit on a line at each size.
        /// </summary>
        public const int Columns =
            (TextureWidth - (Margin * 2)) / Advance;
        public const int TitleColumns =
            (TextureWidth - (Margin * 2)) / (Advance * 2);

        /// <summary>Lines the hero's own text may run to.</summary>
        public const int EpitaphLines = 3;

        /// <summary>
        /// Brass, and the shadow of a stamped letter in it. Both are
        /// flat: the plate carries its own shading in the texture and
        /// takes the scene's light on top.
        /// </summary>
        internal static readonly Color32 Face =
            new Color32(196, 178, 108, 255);
        internal static readonly Color32 Ink =
            new Color32(48, 42, 24, 255);
        internal static readonly Color32 Rule =
            new Color32(120, 106, 64, 255);

        private const byte Unknown = 0;

        /// <summary>
        /// Five bits a row, seven rows a glyph, high bit on the left.
        /// Written as binary so the shape of every letter is readable
        /// in the source — which is the only way a hand-made font
        /// stays maintainable.
        /// </summary>
        private static readonly Dictionary<char, byte[]> Glyphs =
            new Dictionary<char, byte[]>
            {
                [' '] = new byte[] { 0, 0, 0, 0, 0, 0, 0 },
                ['A'] = Rows(0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001),
                ['B'] = Rows(0b11110, 0b10001, 0b10001, 0b11110, 0b10001, 0b10001, 0b11110),
                ['C'] = Rows(0b01111, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b01111),
                ['D'] = Rows(0b11110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b11110),
                ['E'] = Rows(0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111),
                ['F'] = Rows(0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000),
                ['G'] = Rows(0b01111, 0b10000, 0b10000, 0b10111, 0b10001, 0b10001, 0b01111),
                ['H'] = Rows(0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001),
                ['I'] = Rows(0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b11111),
                ['J'] = Rows(0b00111, 0b00010, 0b00010, 0b00010, 0b00010, 0b10010, 0b01100),
                ['K'] = Rows(0b10001, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010, 0b10001),
                ['L'] = Rows(0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b11111),
                ['M'] = Rows(0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001),
                ['N'] = Rows(0b10001, 0b11001, 0b11001, 0b10101, 0b10011, 0b10011, 0b10001),
                ['O'] = Rows(0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110),
                ['P'] = Rows(0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000, 0b10000),
                ['Q'] = Rows(0b01110, 0b10001, 0b10001, 0b10001, 0b10101, 0b10010, 0b01101),
                ['R'] = Rows(0b11110, 0b10001, 0b10001, 0b11110, 0b10100, 0b10010, 0b10001),
                ['S'] = Rows(0b01111, 0b10000, 0b10000, 0b01110, 0b00001, 0b00001, 0b11110),
                ['T'] = Rows(0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100),
                ['U'] = Rows(0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110),
                ['V'] = Rows(0b10001, 0b10001, 0b10001, 0b10001, 0b01010, 0b01010, 0b00100),
                ['W'] = Rows(0b10001, 0b10001, 0b10001, 0b10101, 0b10101, 0b11011, 0b10001),
                ['X'] = Rows(0b10001, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0b10001),
                ['Y'] = Rows(0b10001, 0b10001, 0b01010, 0b00100, 0b00100, 0b00100, 0b00100),
                ['Z'] = Rows(0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b10000, 0b11111),
                ['0'] = Rows(0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110),
                ['1'] = Rows(0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110),
                ['2'] = Rows(0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111),
                ['3'] = Rows(0b11111, 0b00010, 0b00100, 0b00010, 0b00001, 0b10001, 0b01110),
                ['4'] = Rows(0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010),
                ['5'] = Rows(0b11111, 0b10000, 0b11110, 0b00001, 0b00001, 0b10001, 0b01110),
                ['6'] = Rows(0b00110, 0b01000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110),
                ['7'] = Rows(0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000),
                ['8'] = Rows(0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110),
                ['9'] = Rows(0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00010, 0b01100),
                ['.'] = Rows(0, 0, 0, 0, 0, 0b01100, 0b01100),
                [','] = Rows(0, 0, 0, 0, 0b01100, 0b00100, 0b01000),
                ['-'] = Rows(0, 0, 0, 0b11111, 0, 0, 0),
                ['!'] = Rows(0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0, 0b00100),
                ['?'] = Rows(0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0, 0b00100),
                [':'] = Rows(0, 0b01100, 0b01100, 0, 0b01100, 0b01100, 0),
                [';'] = Rows(0, 0b01100, 0b01100, 0, 0b01100, 0b00100, 0b01000),
                ['\''] = Rows(0b00100, 0b00100, 0b01000, 0, 0, 0, 0),
                ['"'] = Rows(0b01010, 0b01010, 0, 0, 0, 0, 0),
                ['('] = Rows(0b00010, 0b00100, 0b01000, 0b01000, 0b01000, 0b00100, 0b00010),
                [')'] = Rows(0b01000, 0b00100, 0b00010, 0b00010, 0b00010, 0b00100, 0b01000),
                ['/'] = Rows(0b00001, 0b00010, 0b00010, 0b00100, 0b01000, 0b01000, 0b10000),
                ['«'] = Rows(0, 0b00101, 0b01010, 0b10100, 0b01010, 0b00101, 0),
                ['»'] = Rows(0, 0b10100, 0b01010, 0b00101, 0b01010, 0b10100, 0),
                // Cyrillic that is not simply a Latin letter wearing a
                // different name. The dozen that are — А В Е К М Н О Р
                // С Т Х — are aliased below rather than drawn twice.
                ['Б'] = Rows(0b11111, 0b10000, 0b10000, 0b11110, 0b10001, 0b10001, 0b11110),
                ['Г'] = Rows(0b11111, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000),
                ['Д'] = Rows(0b00111, 0b00101, 0b00101, 0b01001, 0b01001, 0b11111, 0b10001),
                ['Ж'] = Rows(0b10101, 0b10101, 0b10101, 0b11111, 0b10101, 0b10101, 0b10101),
                ['З'] = Rows(0b01110, 0b10001, 0b00001, 0b00110, 0b00001, 0b10001, 0b01110),
                ['И'] = Rows(0b10001, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b10001),
                ['Й'] = Rows(0b01110, 0b00000, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001),
                ['Л'] = Rows(0b00111, 0b01001, 0b01001, 0b01001, 0b01001, 0b01001, 0b10001),
                ['П'] = Rows(0b11111, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001),
                ['У'] = Rows(0b10001, 0b10001, 0b10001, 0b01111, 0b00001, 0b00010, 0b01100),
                ['Ф'] = Rows(0b00100, 0b01110, 0b10101, 0b10101, 0b10101, 0b01110, 0b00100),
                ['Ц'] = Rows(0b10010, 0b10010, 0b10010, 0b10010, 0b10010, 0b11111, 0b00001),
                ['Ч'] = Rows(0b10001, 0b10001, 0b10001, 0b01111, 0b00001, 0b00001, 0b00001),
                ['Ш'] = Rows(0b10101, 0b10101, 0b10101, 0b10101, 0b10101, 0b10101, 0b11111),
                ['Щ'] = Rows(0b10101, 0b10101, 0b10101, 0b10101, 0b10101, 0b11111, 0b00001),
                ['Ъ'] = Rows(0b11000, 0b01000, 0b01000, 0b01110, 0b01001, 0b01001, 0b01110),
                ['Ы'] = Rows(0b10001, 0b10001, 0b10001, 0b11101, 0b10011, 0b10011, 0b11101),
                ['Ь'] = Rows(0b10000, 0b10000, 0b10000, 0b11110, 0b10001, 0b10001, 0b11110),
                ['Э'] = Rows(0b01110, 0b10001, 0b00001, 0b00111, 0b00001, 0b10001, 0b01110),
                ['Ю'] = Rows(0b10010, 0b10101, 0b10101, 0b11101, 0b10101, 0b10101, 0b10010),
                ['Я'] = Rows(0b01111, 0b10001, 0b10001, 0b01111, 0b00101, 0b01001, 0b10001)
            };

        /// <summary>
        /// The Cyrillic letters whose capitals are the Latin shapes
        /// exactly. Aliasing them keeps one drawing per shape, so a
        /// fix to `О` cannot leave `O` behind.
        /// </summary>
        private static readonly Dictionary<char, char> Aliases =
            new Dictionary<char, char>
            {
                ['А'] = 'A',
                ['В'] = 'B',
                ['Е'] = 'E',
                ['Ё'] = 'E',
                ['К'] = 'K',
                ['М'] = 'M',
                ['Н'] = 'H',
                ['О'] = 'O',
                ['Р'] = 'P',
                ['С'] = 'C',
                ['Т'] = 'T',
                ['Х'] = 'X'
            };

        /// <summary>True when the board can carry this character as
        /// typed, before any folding.</summary>
        public static bool Supports(char glyph)
        {
            char upper = char.ToUpperInvariant(glyph);
            return Glyphs.ContainsKey(upper) ||
                   Aliases.ContainsKey(upper);
        }

        /// <summary>
        /// What the plate will actually carry: upper case, because a
        /// stamped board has no lower case, and anything the font
        /// cannot cut replaced by a mark that says so rather than
        /// silently dropped.
        /// </summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var built = new System.Text.StringBuilder(text.Length);
            for (int index = 0; index < text.Length; index++)
            {
                char upper = char.ToUpperInvariant(text[index]);
                built.Append(Supports(upper) ? upper : '?');
            }

            return built.ToString();
        }

        /// <summary>
        /// Breaks a line to the plate's width on word boundaries, and
        /// hard-breaks a single word too long to fit rather than
        /// letting it run off the brass.
        /// </summary>
        public static IReadOnlyList<string> WrapLines(
            string text,
            int columns,
            int maximumLines)
        {
            if (columns <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(columns));
            }

            var lines = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return lines;
            }

            string[] words = text.Split(
                new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            string current = string.Empty;
            for (int index = 0;
                 index < words.Length && lines.Count < maximumLines;
                 index++)
            {
                string word = words[index];
                while (word.Length > columns &&
                       lines.Count < maximumLines)
                {
                    if (current.Length > 0)
                    {
                        lines.Add(current);
                        current = string.Empty;
                        continue;
                    }

                    lines.Add(word.Substring(0, columns));
                    word = word.Substring(columns);
                }

                if (lines.Count >= maximumLines)
                {
                    return lines;
                }

                string joined = current.Length == 0
                    ? word
                    : current + " " + word;
                if (joined.Length <= columns)
                {
                    current = joined;
                    continue;
                }

                lines.Add(current);
                current = word;
            }

            if (current.Length > 0 && lines.Count < maximumLines)
            {
                lines.Add(current);
            }

            return lines;
        }

        /// <summary>
        /// Stamps the three lines into a plate. The texture is per
        /// grave because the last line is per player; the material it
        /// goes on is still the shared one, handed the map through a
        /// property block.
        /// </summary>
        public static Texture2D Create(
            string name,
            string years,
            string epitaph)
        {
            var pixels = new Color32[TextureWidth * TextureHeight];
            for (int index = 0; index < pixels.Length; index++)
            {
                pixels[index] = Face;
            }

            int cursor = 12;
            DrawCentered(pixels, Normalize(name), cursor, 2);
            cursor += (GlyphHeight * 2) + 6;
            DrawCentered(pixels, Normalize(years), cursor, 1);
            cursor += GlyphHeight + 5;

            for (int x = TextureWidth / 4;
                 x < TextureWidth - (TextureWidth / 4);
                 x++)
            {
                Plot(pixels, x, cursor, Rule);
            }

            cursor += 6;
            IReadOnlyList<string> lines = WrapLines(
                Normalize(epitaph),
                Columns,
                EpitaphLines);
            for (int line = 0; line < lines.Count; line++)
            {
                DrawCentered(pixels, lines[line], cursor, 1);
                cursor += GlyphHeight + 2;
            }

            var texture = new Texture2D(
                TextureWidth,
                TextureHeight,
                TextureFormat.RGBA32,
                false)
            {
                name = "Grave Plaque",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static void DrawCentered(
            Color32[] pixels,
            string text,
            int top,
            int scale)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            int width = text.Length * Advance * scale;
            int left = (TextureWidth - width) / 2;
            for (int index = 0; index < text.Length; index++)
            {
                DrawGlyph(
                    pixels,
                    text[index],
                    left + (index * Advance * scale),
                    top,
                    scale);
            }
        }

        private static void DrawGlyph(
            Color32[] pixels,
            char glyph,
            int left,
            int top,
            int scale)
        {
            if (!TryGetRows(glyph, out byte[] rows))
            {
                return;
            }

            for (int row = 0; row < GlyphHeight; row++)
            {
                for (int column = 0; column < GlyphWidth; column++)
                {
                    int bit = GlyphWidth - 1 - column;
                    if ((rows[row] & (1 << bit)) == 0)
                    {
                        continue;
                    }

                    for (int y = 0; y < scale; y++)
                    {
                        for (int x = 0; x < scale; x++)
                        {
                            Plot(
                                pixels,
                                left + (column * scale) + x,
                                top + (row * scale) + y,
                                Ink);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// One pixel, counting rows from the top of the plate the way
        /// the layout above reads, rather than from the bottom the way
        /// a texture stores them.
        /// </summary>
        private static void Plot(
            Color32[] pixels,
            int x,
            int top,
            Color32 colour)
        {
            if (x < 0 || x >= TextureWidth ||
                top < 0 || top >= TextureHeight)
            {
                return;
            }

            pixels[((TextureHeight - 1 - top) * TextureWidth) + x] =
                colour;
        }

        private static bool TryGetRows(char glyph, out byte[] rows)
        {
            char upper = char.ToUpperInvariant(glyph);
            if (Aliases.TryGetValue(upper, out char aliased))
            {
                upper = aliased;
            }

            return Glyphs.TryGetValue(upper, out rows);
        }

        private static byte[] Rows(
            int r0,
            int r1,
            int r2,
            int r3,
            int r4,
            int r5,
            int r6)
        {
            return new[]
            {
                (byte)r0,
                (byte)r1,
                (byte)r2,
                (byte)r3,
                (byte)r4,
                (byte)r5,
                (byte)r6
            };
        }
    }
}
