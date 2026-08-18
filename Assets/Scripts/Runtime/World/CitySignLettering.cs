using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One axis-aligned stroke of a signage glyph.</summary>
    public readonly struct SignSegmentRect
    {
        public SignSegmentRect(Vector2 center, Vector2 size)
        {
            Center = center;
            Size = size;
        }

        public Vector2 Center { get; }
        public Vector2 Size { get; }
    }

    /// <summary>
    /// Blocky segment lettering for the city's signage: every glyph is
    /// a handful of axis-aligned strokes in a unit cell, laid out on a
    /// facade plane by the caller. Pure geometry — EditMode-testable —
    /// covering exactly the glyphs the city's signs actually spell.
    /// </summary>
    public static class CitySignLettering
    {
        /// <summary>Stroke thickness as a share of the glyph cell.</summary>
        public const float Stroke = 0.18f;

        private const float T = Stroke;
        private const float H = T * 0.5f;

        private static readonly Dictionary<char, float[][]> Glyphs =
            new Dictionary<char, float[][]>
            {
                // Strokes as {centerX, centerY, width, height} in a
                // unit cell spanning [-0.5, 0.5] on both axes.
                ['П'] = new[]
                {
                    new[] { 0f, 0.5f - H, 1f, T },
                    new[] { -0.5f + H, 0f, T, 1f },
                    new[] { 0.5f - H, 0f, T, 1f },
                },
                ['Р'] = new[]
                {
                    new[] { -0.5f + H, 0f, T, 1f },
                    new[] { 0f, 0.5f - H, 1f, T },
                    new[] { 0f, 0f, 1f, T },
                    new[] { 0.5f - H, 0.25f, T, 0.5f },
                },
                ['О'] = new[]
                {
                    new[] { 0f, 0.5f - H, 1f, T },
                    new[] { 0f, -0.5f + H, 1f, T },
                    new[] { -0.5f + H, 0f, T, 1f },
                    new[] { 0.5f - H, 0f, T, 1f },
                },
                ['Д'] = new[]
                {
                    new[] { 0f, 0.5f - H, 0.64f, T },
                    new[] { -0.30f, 0.09f, T, 0.82f },
                    new[] { 0.30f, 0.09f, T, 0.82f },
                    new[] { 0f, -0.32f, 1f, T },
                    new[] { -0.5f + H, -0.43f, T, 0.14f },
                    new[] { 0.5f - H, -0.43f, T, 0.14f },
                },
                ['У'] = new[]
                {
                    new[] { -0.5f + H, 0.25f, T, 0.5f },
                    new[] { 0.5f - H, 0.25f, T, 0.5f },
                    new[] { 0f, 0f, 1f, T },
                    new[] { 0f, -0.25f, T, 0.5f },
                },
                ['К'] = new[]
                {
                    new[] { -0.5f + H, 0f, T, 1f },
                    new[] { -0.24f, 0f, 0.52f, T },
                    new[] { 0.20f, 0.17f, 0.44f, T },
                    new[] { 0.20f, -0.17f, 0.44f, T },
                    new[] { 0.5f - H, 0.33f, T, 0.34f },
                    new[] { 0.5f - H, -0.33f, T, 0.34f },
                },
                ['Т'] = new[]
                {
                    new[] { 0f, 0.5f - H, 1f, T },
                    new[] { 0f, -0.09f, T, 0.82f },
                },
                ['Ы'] = new[]
                {
                    new[] { -0.5f + H, 0f, T, 1f },
                    new[] { -0.21f, 0f, 0.58f, T },
                    new[] { -0.21f, -0.5f + H, 0.58f, T },
                    new[] { 0.08f, -0.25f, T, 0.5f },
                    new[] { 0.5f - H, 0f, T, 1f },
                },
                // А and Л are the boat station's: «ПРОКАТ ЛОДОК». Both
                // are splayed legs in this font, which has no diagonal
                // stroke - the same compromise Д already makes, and at
                // a sign's reading distance through the composite the
                // stepped leg and a true diagonal are one shape.
                ['А'] = new[]
                {
                    new[] { 0f, 0.5f - H, 0.72f, T },
                    new[] { -0.26f, 0.09f, T, 0.82f },
                    new[] { 0.26f, 0.09f, T, 0.82f },
                    new[] { -0.5f + H, -0.30f, T, 0.40f },
                    new[] { 0.5f - H, -0.30f, T, 0.40f },
                    new[] { 0f, -0.08f, 0.58f, T },
                },
                ['Л'] = new[]
                {
                    new[] { 0.06f, 0.5f - H, 0.88f, T },
                    new[] { -0.20f, 0.09f, T, 0.82f },
                    new[] { 0.5f - H, 0f, T, 1f },
                    new[] { -0.5f + H, -0.30f, T, 0.40f },
                },
                ['7'] = new[]
                {
                    new[] { 0f, 0.5f - H, 1f, T },
                    new[] { 0.5f - H, 0f, T, 1f },
                },
            };

        public static bool SupportsGlyph(char glyph)
        {
            return glyph == ' ' || Glyphs.ContainsKey(glyph);
        }

        /// <summary>
        /// Lays a word out on a plane: glyph cells of
        /// <paramref name="cellWidth"/> x <paramref name="cellHeight"/>
        /// stepped by <paramref name="advance"/>, the whole word
        /// centered on the origin. Spaces advance without strokes.
        /// </summary>
        public static IReadOnlyList<SignSegmentRect> Layout(
            string word,
            float cellWidth,
            float cellHeight,
            float advance)
        {
            if (string.IsNullOrEmpty(word))
            {
                throw new ArgumentException(
                    "A sign needs at least one glyph.",
                    nameof(word));
            }

            if (cellWidth <= 0f || cellHeight <= 0f || advance <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cellWidth),
                    "Sign glyph cells and advance must be positive.");
            }

            var segments = new List<SignSegmentRect>(word.Length * 4);
            float firstCenter = -(word.Length - 1) * advance * 0.5f;
            for (int index = 0; index < word.Length; index++)
            {
                char glyph = word[index];
                if (glyph == ' ')
                {
                    continue;
                }

                if (!Glyphs.TryGetValue(glyph, out float[][] strokes))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(word),
                        $"The city sign font has no glyph '{glyph}'.");
                }

                float letterCenter = firstCenter + index * advance;
                for (int stroke = 0; stroke < strokes.Length; stroke++)
                {
                    float[] rect = strokes[stroke];
                    segments.Add(new SignSegmentRect(
                        new Vector2(
                            letterCenter + rect[0] * cellWidth,
                            rect[1] * cellHeight),
                        new Vector2(
                            rect[2] * cellWidth,
                            rect[3] * cellHeight)));
                }
            }

            return segments;
        }
    }
}
