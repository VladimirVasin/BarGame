using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace BarPromenade
{
    internal readonly struct CompositionStep
    {
        public CompositionStep(string phase, float progress)
        {
            Phase = phase;
            Progress = Math.Max(0f, Math.Min(1f, progress));
        }

        public string Phase { get; }
        public float Progress { get; }
    }

    /// <summary>
    /// One ordered construction path for synchronous authoring and staged
    /// travel. All Unity object work stays on the main thread. A stage is
    /// indivisible; the budget yields between stages, never halfway through
    /// an asset's initialization.
    /// </summary>
    internal sealed class RuntimeComposition : IDisposable
    {
        internal const double FrameBudgetMilliseconds = 8d;
        private readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();

        public RuntimeComposition(IEnumerator steps)
        {
            stack.Push(steps ?? throw new ArgumentNullException(nameof(steps)));
        }

        public bool AdvanceFrame(Action<CompositionStep> report,
            double budgetMilliseconds = FrameBudgetMilliseconds)
        {
            long started = Stopwatch.GetTimestamp();
            while (stack.Count > 0)
            {
                IEnumerator current = stack.Peek();
                if (!current.MoveNext())
                {
                    stack.Pop();
                    (current as IDisposable)?.Dispose();
                    continue;
                }

                if (current.Current is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }

                if (current.Current is CompositionStep step)
                {
                    report?.Invoke(step);
                }

                double elapsed = (Stopwatch.GetTimestamp() - started) *
                    1000d / Stopwatch.Frequency;
                if (elapsed >= budgetMilliseconds)
                {
                    return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            while (stack.Count > 0)
            {
                (stack.Pop() as IDisposable)?.Dispose();
            }
        }

        internal static void RunSynchronously(IEnumerator steps)
        {
            using (var operation = new RuntimeComposition(steps))
            {
                while (operation.AdvanceFrame(null, double.PositiveInfinity)) { }
            }
        }

        internal static IEnumerator Range(IEnumerator steps, float start, float end)
        {
            try
            {
                while (steps.MoveNext())
                {
                    if (steps.Current is CompositionStep step)
                    {
                        yield return new CompositionStep(step.Phase,
                            start + (end - start) * step.Progress);
                    }
                    else
                    {
                        yield return steps.Current;
                    }
                }
            }
            finally
            {
                (steps as IDisposable)?.Dispose();
            }
        }
    }
}
