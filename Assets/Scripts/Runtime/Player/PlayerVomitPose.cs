using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What a bout of vomiting asks of the body this frame: how far the
    /// head and neck are pitched down over the ground, how far the torso
    /// is doubled over, how deep the knees give, whether the hands are
    /// braced on the knees, whether the right hand is wiping the mouth,
    /// how hard the stream is running right now, how much of a heave is
    /// going through him and where the stomach's pump is in its beat.
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
            : this(active, headDownDegrees, flow, spasm, 0f, 0f, 0f, 0f, 0f)
        {
        }

        public PlayerVomitPose(
            bool active,
            float headDownDegrees,
            float flow,
            float spasm,
            float torsoPitchDegrees,
            float crouchMetres,
            float braceWeight,
            float wipeWeight,
            float pump)
        {
            Active = active;
            HeadDownDegrees = Mathf.Max(0f, headDownDegrees);
            Flow = Mathf.Clamp01(flow);
            Spasm = Mathf.Clamp01(spasm);
            TorsoPitchDegrees = Mathf.Max(0f, torsoPitchDegrees);
            CrouchMetres = Mathf.Max(0f, crouchMetres);
            BraceWeight = Mathf.Clamp01(braceWeight);
            WipeWeight = Mathf.Clamp01(wipeWeight);
            Pump = Mathf.Clamp01(pump);
        }

        public static PlayerVomitPose None => default;

        /// <summary>A bout is in progress, head-down included.</summary>
        public bool Active { get; }

        /// <summary>Extra forward pitch of the head and neck together, degrees, never negative.</summary>
        public float HeadDownDegrees { get; }

        /// <summary>The stream's strength, 0 dry .. 1 the first gush.</summary>
        public float Flow { get; }

        /// <summary>The heave's envelope at the start of a burst, 0..1.</summary>
        public float Spasm { get; }

        /// <summary>Extra forward pitch of the spine and chest — doubled over — degrees.</summary>
        public float TorsoPitchDegrees { get; }

        /// <summary>How far the knees give under him, metres of pelvis drop.</summary>
        public float CrouchMetres { get; }

        /// <summary>Both hands braced on the knees, 0..1.</summary>
        public float BraceWeight { get; }

        /// <summary>The right hand wiping the mouth as the head comes up, 0..1.</summary>
        public float WipeWeight { get; }

        /// <summary>Where the stomach's pump is in its beat while the stream runs, 0..1; zero between bursts.</summary>
        public float Pump { get; }

        public bool IsNone =>
            !Active &&
            HeadDownDegrees <= 0f &&
            Flow <= 0f &&
            Spasm <= 0f &&
            TorsoPitchDegrees <= 0f &&
            CrouchMetres <= 0f &&
            BraceWeight <= 0f &&
            WipeWeight <= 0f &&
            Pump <= 0f;
    }

    /// <summary>A presentation that can draw the bout over the body.</summary>
    public interface IPlayerVomitPresentation
    {
        void SetVomit(in PlayerVomitPose pose);
    }
}
