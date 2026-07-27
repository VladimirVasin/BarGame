using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed class BeerPongProjection
    {
        public const float LogicalWidth = 640f;
        public const float LogicalHeight = 360f;
        public const float TableCenterX = 320f;
        public const float NearSurfaceY = 310f;
        public const float FarSurfaceY = 128f;
        public const float NearHalfWidth = 286f;
        public const float FarHalfWidth = 96f;

        private const float PerspectiveDistance = 3f;
        private const float NearHeightPixelsPerUnit = 94f;
        private const float FarHeightPixelsPerUnit = 47f;
        private const float NearScale = 1f;
        private const float FarScale = 0.58f;

        private readonly BeerPongTableLayout layout;

        public BeerPongProjection(BeerPongTableLayout tableLayout = null)
        {
            layout = tableLayout ?? BeerPongTableLayout.Default;
        }

        public BeerPongTableLayout Layout => layout;

        public Vector2 Project(Vector3 tablePosition)
        {
            float depth = GetPerspectiveDepth(tablePosition.z);
            float halfWidth = Mathf.Lerp(
                NearHalfWidth,
                FarHalfWidth,
                depth);
            float normalizedX =
                tablePosition.x / layout.TableHalfWidth;
            float surfaceY = Mathf.Lerp(
                NearSurfaceY,
                FarSurfaceY,
                depth);
            float heightScale = Mathf.Lerp(
                NearHeightPixelsPerUnit,
                FarHeightPixelsPerUnit,
                depth);
            return new Vector2(
                TableCenterX + normalizedX * halfWidth,
                surfaceY -
                (tablePosition.y - layout.TableSurfaceY) *
                heightScale);
        }

        public Vector2 ProjectSurface(float x, float z)
        {
            return Project(new Vector3(
                x,
                layout.TableSurfaceY,
                z));
        }

        public float GetProjectedScale(float z)
        {
            return Mathf.Lerp(
                NearScale,
                FarScale,
                GetPerspectiveDepth(z));
        }

        public Rect ProjectBall(Vector3 position)
        {
            float side = Mathf.Max(
                8f,
                18f * GetProjectedScale(position.z));
            Vector2 center = Project(position);
            return PixelRectAround(center, side, side);
        }

        public Rect ProjectBallShadow(Vector3 position)
        {
            float scale = GetProjectedScale(position.z);
            float width = Mathf.Max(7f, 21f * scale);
            float height = Mathf.Max(3f, 8f * scale);
            Vector2 center = ProjectSurface(
                position.x,
                position.z);
            return PixelRectAround(center, width, height);
        }

        public Rect ProjectCup(BeerPongCupDefinition cup)
        {
            float scale = GetProjectedScale(cup.MouthCenter.z);
            float width = 43f * scale;
            float height = 54f * scale;
            Vector2 basePosition = Project(cup.BaseCenter);
            return RetroUiTheme.SnapRect(new Rect(
                basePosition.x - width * 0.5f,
                basePosition.y - height + 3f,
                width,
                height));
        }

        public Vector3 CalculateLandingPoint(
            float yawDegrees,
            float pitchDegrees,
            float power)
        {
            Vector3 origin = layout.ThrowOrigin;
            Vector3 velocity = BeerPongAim.ToVelocity(
                yawDegrees,
                pitchDegrees,
                power);
            float targetY =
                layout.TableSurfaceY + layout.BallRadius;
            float gravity =
                BeerPongPhysicsSettings.Default.Gravity;
            float discriminant =
                velocity.y * velocity.y -
                2f * gravity * (origin.y - targetY);
            if (discriminant < 0f)
            {
                return origin;
            }

            float time =
                (-velocity.y - Mathf.Sqrt(discriminant)) /
                gravity;
            if (!BeerPongMath.IsFinitePositive(time))
            {
                return origin;
            }

            return new Vector3(
                origin.x + velocity.x * time,
                targetY,
                origin.z + velocity.z * time);
        }

        public Vector3 CalculateBallisticPoint(
            float yawDegrees,
            float pitchDegrees,
            float power,
            float time)
        {
            if (!BeerPongMath.IsFiniteNonNegative(time))
            {
                throw new ArgumentOutOfRangeException(nameof(time));
            }

            Vector3 origin = layout.ThrowOrigin;
            Vector3 velocity = BeerPongAim.ToVelocity(
                yawDegrees,
                pitchDegrees,
                power);
            return origin +
                   velocity * time +
                   Vector3.up *
                   (0.5f *
                    BeerPongPhysicsSettings.Default.Gravity *
                    time *
                    time);
        }

        public Vector3 ClampToTable(Vector3 position)
        {
            position.x = Mathf.Clamp(
                position.x,
                -layout.TableHalfWidth,
                layout.TableHalfWidth);
            position.z = Mathf.Clamp(
                position.z,
                layout.TableNearZ,
                layout.TableFarZ);
            return position;
        }

        private float GetPerspectiveDepth(float z)
        {
            float distance = Mathf.Clamp(
                z - layout.TableNearZ,
                0f,
                layout.TableFarZ - layout.TableNearZ);
            float totalDistance =
                layout.TableFarZ - layout.TableNearZ;
            float warped = distance /
                           (distance + PerspectiveDistance);
            float farWarped = totalDistance /
                              (totalDistance +
                               PerspectiveDistance);
            return farWarped <= 0f
                ? 0f
                : Mathf.Clamp01(warped / farWarped);
        }

        private static Rect PixelRectAround(
            Vector2 center,
            float width,
            float height)
        {
            return RetroUiTheme.SnapRect(new Rect(
                center.x - width * 0.5f,
                center.y - height * 0.5f,
                width,
                height));
        }
    }
}
