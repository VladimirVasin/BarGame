using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The black the journey passes through.
    ///
    /// The city tunnel goes dark and the mountain tunnel comes out of the
    /// dark, and between the two is a `LoadSceneMode.Single` boundary that
    /// destroys one world and builds another. Something has to cover it.
    ///
    /// This is not the fade `ai/contextual-animation-standard.md` forbids.
    /// That rule is about hiding an endpoint MISMATCH between two clips -
    /// manufacturing a cut because a pose does not meet the next one. Nothing
    /// is being concealed here: the hero is in the same seat, in the same
    /// pose, in the same car on both sides of it. What it covers is a scene
    /// load, which is the identical job `DoorTransitionRoot` already does for
    /// every ordinary door in the game, and this is that overlay's own shape -
    /// a full-screen fill at <c>GUI.depth = -1000</c> on repaint only.
    ///
    /// Unscaled on purpose: the pause menu freezes scaled time, and a fade
    /// that stopped halfway because somebody opened the options would leave a
    /// grey screen with no way back.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(400)]
    public sealed class LastRouteRideFadeView : MonoBehaviour
    {
        public const string RuntimeObjectName = "Last Route Ride Fade";

        /// <summary>How long the screen takes to go under. Slow enough to
        /// read as the tunnel swallowing the car rather than as a cut.
        /// </summary>
        public const float FadeOutSeconds = 1.4f;

        /// <summary>And how long it takes to come back, which is quicker: the
        /// mountain's tunnel is nine metres deep and the car is already
        /// through most of it.</summary>
        public const float FadeInSeconds = 0.9f;

        private float opacity;
        private float target;
        private float rate = 1f;

        /// <summary>How black the screen currently is, in `[0, 1]`.</summary>
        public float Opacity => opacity;

        public bool IsFullyBlack => opacity >= 0.999f;
        public bool IsClear => opacity <= 0.001f;

        public static LastRouteRideFadeView Create(Transform parent)
        {
            var host = new GameObject(RuntimeObjectName);
            if (parent != null)
            {
                host.transform.SetParent(parent, false);
            }

            return host.AddComponent<LastRouteRideFadeView>();
        }

        /// <summary>Starts black, for the side of the load that has to come
        /// out of it.</summary>
        public void SetBlack()
        {
            opacity = 1f;
            target = 1f;
        }

        public void FadeOut()
        {
            target = 1f;
            rate = FadeOutSeconds > 0f ? 1f / FadeOutSeconds : 1f;
        }

        public void FadeIn()
        {
            target = 0f;
            rate = FadeInSeconds > 0f ? 1f / FadeInSeconds : 1f;
        }

        private void Update()
        {
            opacity = Mathf.MoveTowards(
                opacity,
                target,
                rate * Time.unscaledDeltaTime);
        }

        private void OnGUI()
        {
            if (opacity <= 0f || Event.current.type != EventType.Repaint)
            {
                return;
            }

            Color previousColor = GUI.color;
            int previousDepth = GUI.depth;
            GUI.depth = -1000;
            GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(opacity));
            GUI.DrawTexture(
                new Rect(0f, 0f, Screen.width, Screen.height),
                Texture2D.whiteTexture,
                ScaleMode.StretchToFill);
            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }
    }
}
