using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    /// <summary>
    /// Pumps one destination's registered construction iterator a frame at
    /// a time on either travel path: under the area loading bar or behind
    /// the door's black. It is created before the destination activates, so
    /// its tempo and listener pauses already hold when the root registers
    /// from Awake, and disposed by the service that owns the transition,
    /// which also decides what an abort does with unfinished work.
    /// While it pumps, the growing world is not drawn every frame: only the
    /// overlay is meant to be seen, and in the editor each draw of the
    /// half-built city cost 14-27 ms on top of the build itself.
    /// </summary>
    internal sealed class CompositionDriver : IDisposable
    {
        /// <summary>
        /// Frames between draws while a composition runs. Only a game view
        /// is affected; batch mode presents nothing, so it changes nothing
        /// there. The IMGUI overlay repaints at this rate too, which a
        /// static black or a bar moving a few times a second can bear.
        /// </summary>
        internal const int ThrottledRenderFrameInterval = 4;

        private static CompositionDriver active;

        private readonly string path;
        private readonly string destination;
        private readonly bool previousListenerPause;
        private readonly int previousRenderFrameInterval;
        private RuntimeComposition composition;
        private MonoBehaviour owner;
        private IDisposable tempoPause;
        private bool holdsPauses;
        private bool throttlesRendering;
        private long pumpStarted;
        private double advanceMs;

        public CompositionDriver(string path, string destination)
        {
            this.path = path ?? string.Empty;
            this.destination = destination ?? string.Empty;
            tempoPause = GameTimeScaleRuntime.AcquirePause();
            previousListenerPause = AudioListener.pause;
            AudioListener.pause = true;
            holdsPauses = true;
            previousRenderFrameInterval =
                OnDemandRendering.renderFrameInterval;
            OnDemandRendering.renderFrameInterval =
                ThrottledRenderFrameInterval;
            throttlesRendering = true;
            active = this;
        }

        /// <summary>
        /// True from a root's registration until the owning service releases
        /// the driver, on whichever path is composing.
        /// </summary>
        public static bool IsComposing =>
            active != null && active.composition != null;

        public bool HasComposition => composition != null;
        public bool Registered { get; private set; }
        /// <summary>Set once the iterator ran to its end, pumped or drained.</summary>
        public bool Completed { get; private set; }
        public Exception Failure { get; private set; }
        public int Frames { get; private set; }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            active = null;
        }

        public void Register(MonoBehaviour owner, IEnumerator steps)
        {
            if (composition != null)
            {
                throw new InvalidOperationException(
                    "The destination has already registered its composition.");
            }

            this.owner = owner != null
                ? owner
                : throw new ArgumentNullException(nameof(owner));
            composition = new RuntimeComposition(steps);
            Registered = true;
        }

        /// <summary>
        /// One frame's worth of stages; true while more remain. A throwing
        /// stage or a destroyed owner ends the work here: the exception is
        /// logged, the iterator disposed and <see cref="Failure"/> set.
        /// </summary>
        public bool AdvanceFrame(Action<CompositionStep> report = null)
        {
            if (composition == null || Completed)
            {
                return false;
            }

            if (!TryRun(report, RuntimeComposition.FrameBudgetMilliseconds,
                    out bool more))
            {
                return false;
            }

            if (!more)
            {
                Finish(false);
            }

            return more;
        }

        /// <summary>
        /// Runs whatever is left in one go, for an abort with no world to go
        /// back to: a half-built destination is worse than one long frame.
        /// A destroyed owner has nothing left to build into, so its iterator
        /// is disposed instead.
        /// </summary>
        public void Drain()
        {
            if (composition == null || Completed)
            {
                return;
            }

            if (owner == null)
            {
                ReleaseComposition();
                return;
            }

            if (TryRun(null, double.PositiveInfinity, out _))
            {
                Finish(true);
            }
        }

        public void Dispose()
        {
            ReleaseComposition();
            if (holdsPauses)
            {
                holdsPauses = false;
                tempoPause?.Dispose();
                tempoPause = null;
                AudioListener.pause = previousListenerPause;
            }

            if (active == this)
            {
                active = null;
            }
        }

        private bool TryRun(Action<CompositionStep> report,
            double budgetMilliseconds, out bool more)
        {
            more = false;
            if (pumpStarted == 0L)
            {
                pumpStarted = Stopwatch.GetTimestamp();
            }

            try
            {
                if (owner == null)
                {
                    throw new InvalidOperationException(
                        "The destination root was destroyed during composition.");
                }

                long started = Stopwatch.GetTimestamp();
                more = composition.AdvanceFrame(report, budgetMilliseconds);
                advanceMs += (Stopwatch.GetTimestamp() - started) *
                    1000d / Stopwatch.Frequency;
                Frames++;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Failure = exception;
                ReleaseComposition();
                return false;
            }
        }

        private void Finish(bool drained)
        {
            Completed = true;
            // Full-rate drawing returns here, not in Dispose: the owning
            // service keeps its overlay up for one more frame, so the first
            // draw of the finished world - and, in the editor, the shader
            // variants it compiles on first use - lands under the overlay
            // rather than on the first frame the player sees.
            RestoreRendering();
            double wallMs = (Stopwatch.GetTimestamp() - pumpStarted) *
                1000d / Stopwatch.Frequency;
            GameLog.Debug(
                "scene",
                "composition_frames",
                GameLog.Field("destination", destination),
                GameLog.Field("path", path),
                GameLog.Field("frames", Frames),
                GameLog.Field("advance_ms", advanceMs),
                GameLog.Field("wall_ms", wallMs),
                GameLog.Field("overhead_ms", wallMs - advanceMs),
                GameLog.Field("drained", drained),
                GameLog.Field(
                    "target_frame_rate",
                    Application.targetFrameRate));
        }

        private void ReleaseComposition()
        {
            RestoreRendering();
            composition?.Dispose();
            composition = null;
            owner = null;
        }

        /// <summary>
        /// Puts back the interval found at construction - 1 on every path
        /// the game takes - once, whether the build finished, failed, lost
        /// its owner or the driver is disposed without a registration.
        /// </summary>
        private void RestoreRendering()
        {
            if (!throttlesRendering)
            {
                return;
            }

            throttlesRendering = false;
            OnDemandRendering.renderFrameInterval =
                previousRenderFrameInterval;
        }
    }
}
