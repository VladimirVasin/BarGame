using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Chooses the atlas row from the player's camera-horizontal offset.
    /// Separate enter and return thresholds provide center hysteresis.
    /// </summary>
    public sealed class StairwellCatLookSelector
    {
        public const float DefaultSideEnterDistance = 0.42f;
        public const float DefaultCenterReturnDistance = 0.18f;

        private readonly float sideEnterDistance;
        private readonly float centerReturnDistance;

        public StairwellCatLookSelector(
            float sideEnterDistance =
                DefaultSideEnterDistance,
            float centerReturnDistance =
                DefaultCenterReturnDistance)
        {
            this.sideEnterDistance =
                Mathf.Max(0f, sideEnterDistance);
            this.centerReturnDistance = Mathf.Clamp(
                centerReturnDistance,
                0f,
                this.sideEnterDistance);
            Current = StairwellCatLook.Center;
        }

        public StairwellCatLook Current { get; private set; }

        public StairwellCatLook Update(
            Vector3 catPosition,
            Vector3 playerPosition,
            Vector3 cameraRight)
        {
            if (cameraRight.sqrMagnitude <= 0.000001f)
            {
                return Current;
            }

            float projectedOffset = Vector3.Dot(
                playerPosition - catPosition,
                cameraRight.normalized);
            return UpdateProjectedOffset(projectedOffset);
        }

        public StairwellCatLook UpdateProjectedOffset(
            float projectedOffset)
        {
            if (float.IsNaN(projectedOffset) ||
                float.IsInfinity(projectedOffset))
            {
                return Current;
            }

            switch (Current)
            {
                case StairwellCatLook.LookLeft:
                    if (projectedOffset >
                        sideEnterDistance)
                    {
                        Current =
                            StairwellCatLook.LookRight;
                    }
                    else if (projectedOffset >
                             -centerReturnDistance)
                    {
                        Current =
                            StairwellCatLook.Center;
                    }

                    break;
                case StairwellCatLook.LookRight:
                    if (projectedOffset <
                        -sideEnterDistance)
                    {
                        Current =
                            StairwellCatLook.LookLeft;
                    }
                    else if (projectedOffset <
                             centerReturnDistance)
                    {
                        Current =
                            StairwellCatLook.Center;
                    }

                    break;
                default:
                    if (projectedOffset <
                        -sideEnterDistance)
                    {
                        Current =
                            StairwellCatLook.LookLeft;
                    }
                    else if (projectedOffset >
                             sideEnterDistance)
                    {
                        Current =
                            StairwellCatLook.LookRight;
                    }

                    break;
            }

            return Current;
        }

        public void Reset()
        {
            Current = StairwellCatLook.Center;
        }
    }
}
