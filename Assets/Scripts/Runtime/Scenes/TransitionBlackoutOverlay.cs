using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// A full-screen black that outlives a Single load. IMGUI, so it needs
    /// neither a camera nor a scene: the door scene that drew the black
    /// until now dies with the load, and the destination's camera may not
    /// exist yet while its world is pumped frame by frame behind this.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TransitionBlackoutOverlay : MonoBehaviour
    {
        private const int Depth = -1000;

        internal static TransitionBlackoutOverlay Create()
        {
            GameObject host = new GameObject("[Bar Promenade] Transition Blackout");
            DontDestroyOnLoad(host);
            return host.AddComponent<TransitionBlackoutOverlay>();
        }

        internal static void Draw(float opacity)
        {
            if (opacity <= 0f || Event.current.type != EventType.Repaint)
            {
                return;
            }

            Color previousColor = GUI.color;
            int previousDepth = GUI.depth;
            GUI.depth = Depth;
            GUI.color = new Color(0f, 0f, 0f, opacity);
            GUI.DrawTexture(
                new Rect(0f, 0f, Screen.width, Screen.height),
                Texture2D.whiteTexture,
                ScaleMode.StretchToFill);
            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }

        private void OnGUI()
        {
            Draw(1f);
        }
    }
}
