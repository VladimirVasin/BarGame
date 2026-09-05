using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The stomach under the nausea gauge: sixteen pixels of dark green,
    /// drawn in code like every other icon in the interface. The bitmap is
    /// written top row first, the way it is read; the painter's rows grow
    /// upward, so the row index is flipped on the way in.
    /// </summary>
    public static class IntoxicationNauseaIconLibrary
    {
        public const int IconSize = 16;

        public static readonly Color32 Ink = new Color32(18, 32, 20, 255);
        public static readonly Color32 Body = new Color32(46, 84, 52, 255);
        public static readonly Color32 Highlight = new Color32(72, 116, 74, 255);

        /// <summary>
        /// `.` clear, `#` outline, `B` body, `H` highlight. The gullet
        /// comes in at the top left of centre, the greater curve bulges
        /// down and left, and the pylorus leaves to the right.
        /// </summary>
        internal static readonly string[] StomachRows =
        {
            ".....###........",
            ".....#B#........",
            ".....#B#........",
            "...###B###......",
            "..#BBBBBBB#.....",
            ".#BBHBBBBBB#....",
            ".#BHBBBBBBBB#...",
            ".#BBBBBBBBBBB#..",
            ".#BBBBBBBBBBBB#.",
            ".#BBBBBBBBBBBBB#",
            "..#BBBBBBBBBB#B#",
            "..#BBBBBBBBB#.#.",
            "...#BBBBBBB#....",
            "....##BBB##.....",
            "......###.......",
            "................"
        };

        private static Texture2D stomach;

        public static Texture2D GetStomachIcon()
        {
            if (stomach == null)
            {
                stomach = CreateStomachIcon();
            }

            return stomach;
        }

        private static Texture2D CreateStomachIcon()
        {
            var painter = new PixelPainter(IconSize, IconSize);
            for (int row = 0; row < StomachRows.Length && row < IconSize; row++)
            {
                string line = StomachRows[row];
                int y = IconSize - 1 - row;
                for (int x = 0; x < line.Length && x < IconSize; x++)
                {
                    switch (line[x])
                    {
                        case '#':
                            painter.Set(x, y, Ink);
                            break;
                        case 'B':
                            painter.Set(x, y, Body);
                            break;
                        case 'H':
                            painter.Set(x, y, Highlight);
                            break;
                    }
                }
            }

            return painter.CreateTexture("Nausea Stomach Icon");
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetResources()
        {
            if (stomach == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(stomach);
            }
            else
            {
                Object.DestroyImmediate(stomach);
            }

            stomach = null;
        }
    }
}
