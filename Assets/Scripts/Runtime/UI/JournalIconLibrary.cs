using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The notebook in the corner: sixteen pixels of worn paper with a
    /// stitched spine and three ruled lines, drawn in code like every
    /// other icon in the interface. The bitmap is written top row
    /// first, the way it is read; the painter's rows grow upward, so
    /// the row index is flipped on the way in.
    /// </summary>
    public static class JournalIconLibrary
    {
        public const int IconSize = 16;

        public static readonly Color32 Ink = new Color32(24, 22, 18, 255);
        public static readonly Color32 Paper = new Color32(198, 186, 160, 255);
        public static readonly Color32 Spine = new Color32(122, 92, 58, 255);
        public static readonly Color32 Rule = new Color32(126, 116, 96, 255);

        /// <summary>
        /// `.` clear, `#` outline, `P` paper, `S` spine, `R` ruled line.
        /// The spine runs down the left edge and the three rules sit
        /// where writing would be, so the shape reads as a book even at
        /// sixteen pixels.
        /// </summary>
        internal static readonly string[] NotebookRows =
        {
            "................",
            "..############..",
            "..#SS#PPPPPPP#..",
            "..#SS#PPPPPPP#..",
            "..#SS#PRRRRRP#..",
            "..#SS#PPPPPPP#..",
            "..#SS#PRRRRRP#..",
            "..#SS#PPPPPPP#..",
            "..#SS#PRRRRRP#..",
            "..#SS#PPPPPPP#..",
            "..#SS#PRRRRP.#..",
            "..#SS#PPPPPPP#..",
            "..#SS#PPPPPPP#..",
            "..############..",
            "................",
            "................"
        };

        private static Texture2D notebook;

        public static Texture2D GetNotebookIcon()
        {
            if (notebook == null)
            {
                notebook = CreateNotebookIcon();
            }

            return notebook;
        }

        private static Texture2D CreateNotebookIcon()
        {
            var painter = new PixelPainter(IconSize, IconSize);
            for (int row = 0;
                 row < NotebookRows.Length && row < IconSize;
                 row++)
            {
                string line = NotebookRows[row];
                int y = IconSize - 1 - row;
                for (int x = 0; x < line.Length && x < IconSize; x++)
                {
                    switch (line[x])
                    {
                        case '#':
                            painter.Set(x, y, Ink);
                            break;
                        case 'P':
                            painter.Set(x, y, Paper);
                            break;
                        case 'S':
                            painter.Set(x, y, Spine);
                            break;
                        case 'R':
                            painter.Set(x, y, Rule);
                            break;
                    }
                }
            }

            return painter.CreateTexture("Journal Notebook Icon");
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetResources()
        {
            if (notebook == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(notebook);
            }
            else
            {
                Object.DestroyImmediate(notebook);
            }

            notebook = null;
        }
    }
}
