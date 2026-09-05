using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What a bout of vomiting asks of the body this frame: how far the
    /// head is pitched down over the ground, how hard the stream is
    /// running right now, and how much of a heave is going through him.
    /// Exactly <see cref="None"/> costs the presentation nothing — the
    /// model hands it back the moment a bout ends or is cancelled, and
    /// the presentation blends nothing on its own.
    /// </summary>
    public readonly struct PlayerVomitPose
    {
        public PlayerVomitPose(
            bool active,
            float headDownDegrees,
            float flow,
            float spasm)
        {
            Active = active;
            HeadDownDegrees = Mathf.Max(0f, headDownDegrees);
            Flow = Mathf.Clamp01(flow);
            Spasm = Mathf.Clamp01(spasm);
        }

        public static PlayerVomitPose None => default;

        /// <summary>A bout is in progress, head-down included.</summary>
        public bool Active { get; }

        /// <summary>Extra forward pitch of the head, degrees, never negative.</summary>
        public float HeadDownDegrees { get; }

        /// <summary>The stream's strength, 0 dry .. 1 the first gush.</summary>
        public float Flow { get; }

        /// <summary>The heave's envelope at the start of a burst, 0..1.</summary>
        public float Spasm { get; }

        public bool IsNone =>
            !Active && HeadDownDegrees <= 0f && Flow <= 0f && Spasm <= 0f;
    }

    /// <summary>A presentation that can draw the bout over the body.</summary>
    public interface IPlayerVomitPresentation
    {
        void SetVomit(in PlayerVomitPose pose);
    }
}
