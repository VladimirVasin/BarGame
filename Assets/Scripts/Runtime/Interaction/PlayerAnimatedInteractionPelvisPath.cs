using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Authored support changes, sampled in the clip's normalized time.</summary>
    public sealed class PlayerAnimatedInteractionPelvisPath
    {
        public readonly struct Key
        {
            public Key(float progress, Vector3 position)
            {
                Progress = progress;
                Position = position;
            }

            public float Progress { get; }
            public Vector3 Position { get; }
        }

        private readonly Key[] keys;

        public PlayerAnimatedInteractionPelvisPath(params Key[] authoredKeys)
        {
            if (authoredKeys == null || authoredKeys.Length < 2)
                throw new ArgumentException("A pelvis path needs both endpoints.", nameof(authoredKeys));
            keys = (Key[])authoredKeys.Clone();
            if (keys[0].Progress != 0f || keys[keys.Length - 1].Progress != 1f)
                throw new ArgumentException("A pelvis path must cover the entire clip.", nameof(authoredKeys));
            for (int index = 0; index < keys.Length; index++)
            {
                Key key = keys[index];
                if (!IsFinite(key.Progress) || !IsFinite(key.Position.x) ||
                    !IsFinite(key.Position.y) || !IsFinite(key.Position.z) ||
                    (index > 0 && key.Progress <= keys[index - 1].Progress))
                    throw new ArgumentException("Pelvis keys must be finite and ordered.", nameof(authoredKeys));
            }
        }

        public Vector3 Evaluate(Vector3 start, Vector3 end, float progress)
        {
            // A path cannot conceal a mismatch at the ordinary-rig handoff.
            if ((keys[0].Position - start).sqrMagnitude > 0.000001f ||
                (keys[keys.Length - 1].Position - end).sqrMagnitude > 0.000001f)
                throw new InvalidOperationException("Pelvis path endpoints must match the interaction plan.");
            if (progress <= 0f) return start;
            for (int index = 1; index < keys.Length; index++)
            {
                Key next = keys[index];
                if (progress > next.Progress) continue;
                Key previous = keys[index - 1];
                float blend = Mathf.SmoothStep(0f, 1f,
                    (progress - previous.Progress) / (next.Progress - previous.Progress));
                return Vector3.LerpUnclamped(previous.Position, next.Position, blend);
            }
            return end;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
