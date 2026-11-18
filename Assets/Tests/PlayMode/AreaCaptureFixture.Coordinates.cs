using System;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        // Camera.Render omits the on-screen HUD. Stamp the actual staged
        // camera position into the diagnostic image after post-processing.
        private static Rect DrawCaptureCoordinates(RenderTexture target, string scene, Vector3 position)
        {
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(target.width, target.height);
            Rect screen = canvas.LogicalToScreen(PlayerCoordinatesHud.ResolvePanelRect(canvas));
            var panel = new Rect(screen.x, target.height - screen.yMax, screen.width, screen.height);
            float scale = canvas.Scale;
            Font font = RetroUiTheme.InterfaceFont;
            string title = scene + " / Камера";
            string coordinates = FormattableString.Invariant($"X {position.x:F1}   Y {position.y:F1}   Z {position.z:F1}");
            int titleSize = Mathf.RoundToInt(9f * scale);
            int valueSize = Mathf.RoundToInt(11f * scale);
            font.RequestCharactersInTexture(title, titleSize);
            font.RequestCharactersInTexture(coordinates, valueSize);

            GL.PushMatrix();
            try
            {
                GL.LoadPixelMatrix(0, target.width, 0, target.height);
                Fill(panel, new Color(.23f, .22f, .18f, 1f));
                float inset = 2f * scale;
                Fill(Rect.MinMaxRect(panel.xMin + inset, panel.yMin + inset,
                    panel.xMax - inset, panel.yMax - inset), new Color(.055f, .053f, .045f, .96f));
                if (font.material.SetPass(0))
                {
                    float left = panel.xMin + 7f * scale;
                    DrawText(title, titleSize, left, panel.yMin + 23f * scale, RetroUiTheme.Text);
                    DrawText(coordinates, valueSize, left, panel.yMin + 9f * scale, RetroUiTheme.AccentPale);
                }
            }
            finally
            {
                GL.PopMatrix();
            }
            return panel;

            static void Fill(Rect rect, Color color) => Graphics.DrawTexture(rect, Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f), 0, 0, 0, 0, color);

            void DrawText(string text, int size, float x, float baseline, Color color)
            {
                GL.Begin(GL.QUADS);
                GL.Color(color);
                foreach (char character in text)
                {
                    if (!font.GetCharacterInfo(character, out CharacterInfo glyph, size)) continue;
                    Vertex(x + glyph.minX, baseline + glyph.minY, glyph.uvBottomLeft);
                    Vertex(x + glyph.minX, baseline + glyph.maxY, glyph.uvTopLeft);
                    Vertex(x + glyph.maxX, baseline + glyph.maxY, glyph.uvTopRight);
                    Vertex(x + glyph.maxX, baseline + glyph.minY, glyph.uvBottomRight);
                    x += glyph.advance;
                }
                GL.End();
            }

            static void Vertex(float x, float y, Vector2 uv)
            {
                GL.TexCoord2(uv.x, uv.y);
                GL.Vertex3(x, y, 0f);
            }
        }
    }
}
