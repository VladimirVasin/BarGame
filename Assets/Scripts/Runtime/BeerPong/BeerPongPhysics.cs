using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed class BeerPongPhysicsSettings
    {
        private static readonly BeerPongPhysicsSettings defaultSettings =
            new BeerPongPhysicsSettings();

        public BeerPongPhysicsSettings(
            float gravity = -9.81f,
            float airDrag = 0.055f,
            float tableRestitution = 0.62f,
            float tableTangentialRetention = 0.84f,
            float rimRestitution = 0.72f,
            float groundFriction = 2.4f,
            float minimumVerticalBounceSpeed = 0.72f,
            float settledHorizontalSpeed = 0.16f,
            float settledDuration = 0.35f,
            float maxFlightDuration = 5f,
            float outOfBoundsMargin = 1.5f,
            float maximumHeight = 5f)
        {
            if (!BeerPongMath.IsFinite(gravity) || gravity >= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(gravity));
            }

            ValidateNonNegative(airDrag, nameof(airDrag));
            ValidateUnit(tableRestitution, nameof(tableRestitution));
            ValidateUnit(
                tableTangentialRetention,
                nameof(tableTangentialRetention));
            ValidateUnit(rimRestitution, nameof(rimRestitution));
            ValidateNonNegative(groundFriction, nameof(groundFriction));
            ValidateNonNegative(
                minimumVerticalBounceSpeed,
                nameof(minimumVerticalBounceSpeed));
            ValidateNonNegative(
                settledHorizontalSpeed,
                nameof(settledHorizontalSpeed));
            ValidatePositive(settledDuration, nameof(settledDuration));
            ValidatePositive(maxFlightDuration, nameof(maxFlightDuration));
            ValidatePositive(outOfBoundsMargin, nameof(outOfBoundsMargin));
            ValidatePositive(maximumHeight, nameof(maximumHeight));

            Gravity = gravity;
            AirDrag = airDrag;
            TableRestitution = tableRestitution;
            TableTangentialRetention = tableTangentialRetention;
            RimRestitution = rimRestitution;
            GroundFriction = groundFriction;
            MinimumVerticalBounceSpeed = minimumVerticalBounceSpeed;
            SettledHorizontalSpeed = settledHorizontalSpeed;
            SettledDuration = settledDuration;
            MaxFlightDuration = maxFlightDuration;
            OutOfBoundsMargin = outOfBoundsMargin;
            MaximumHeight = maximumHeight;
        }

        public static BeerPongPhysicsSettings Default => defaultSettings;
        public float Gravity { get; }
        public float AirDrag { get; }
        public float TableRestitution { get; }
        public float TableTangentialRetention { get; }
        public float RimRestitution { get; }
        public float GroundFriction { get; }
        public float MinimumVerticalBounceSpeed { get; }
        public float SettledHorizontalSpeed { get; }
        public float SettledDuration { get; }
        public float MaxFlightDuration { get; }
        public float OutOfBoundsMargin { get; }
        public float MaximumHeight { get; }

        private static void ValidateUnit(float value, string argumentName)
        {
            if (!BeerPongMath.IsFinite(value) || value < 0f || value > 1f)
            {
                throw new ArgumentOutOfRangeException(argumentName);
            }
        }

        private static void ValidateNonNegative(
            float value,
            string argumentName)
        {
            if (!BeerPongMath.IsFiniteNonNegative(value))
            {
                throw new ArgumentOutOfRangeException(argumentName);
            }
        }

        private static void ValidatePositive(
            float value,
            string argumentName)
        {
            if (!BeerPongMath.IsFinitePositive(value))
            {
                throw new ArgumentOutOfRangeException(argumentName);
            }
        }
    }

    public sealed class BeerPongPhysicsSimulation
    {
        public const float FixedDeltaTime = 1f / 120f;

        private const double FixedStepSeconds = 1d / 120d;
        private const double AccumulatorEpsilon = 0.000000001d;
        private const float RimVerticalNormalWeight = 1.5f;
        private const float RimCollisionCooldown = 0.04f;

        private readonly BeerPongTableLayout layout;
        private readonly BeerPongPhysicsSettings settings;

        private BeerPongBallStatus status;
        private BeerPongFlightResult result;
        private Vector3 previousPosition;
        private Vector3 position;
        private Vector3 velocity;
        private int activeCupMask;
        private int tableBounceCount;
        private int rimBounceCount;
        private int lastRimCupIndex = -1;
        private float lastRimCollisionTime = float.NegativeInfinity;
        private float elapsedTime;
        private float settledTime;
        private bool isGrounded;
        private double accumulator;

        public BeerPongPhysicsSimulation(
            BeerPongTableLayout tableLayout = null,
            BeerPongPhysicsSettings physicsSettings = null)
        {
            layout = tableLayout ?? BeerPongTableLayout.Default;
            settings = physicsSettings ?? BeerPongPhysicsSettings.Default;
            Reset();
        }

        public BeerPongTableLayout Layout => layout;
        public BeerPongPhysicsSettings Settings => settings;
        public BeerPongBallStatus Status => status;
        public bool IsInFlight => status == BeerPongBallStatus.InFlight;
        public bool IsComplete =>
            status == BeerPongBallStatus.Sunk ||
            status == BeerPongBallStatus.Missed;
        public float InterpolationAlpha =>
            Mathf.Clamp01((float)(accumulator / FixedStepSeconds));
        public Vector3 InterpolatedPosition =>
            IsInFlight
                ? Vector3.Lerp(
                    previousPosition,
                    position,
                    InterpolationAlpha)
                : position;
        public BeerPongBallSnapshot Snapshot =>
            new BeerPongBallSnapshot(
                status,
                position,
                velocity,
                elapsedTime,
                tableBounceCount,
                rimBounceCount);
        public BeerPongFlightResult Result
        {
            get
            {
                if (!IsComplete)
                {
                    throw new InvalidOperationException(
                        "The flight has not produced a result yet.");
                }

                return result;
            }
        }

        public void Launch(
            Vector3 launchPosition,
            Vector3 initialVelocity,
            int standingCupMask)
        {
            if (!BeerPongMath.IsFinite(launchPosition))
            {
                throw new ArgumentException(
                    "Launch position must be finite.",
                    nameof(launchPosition));
            }

            if (!BeerPongMath.IsFinite(initialVelocity) ||
                initialVelocity.sqrMagnitude <= 0.000001f)
            {
                throw new ArgumentException(
                    "Initial velocity must be finite and non-zero.",
                    nameof(initialVelocity));
            }

            if (standingCupMask < 0 ||
                (standingCupMask & ~layout.AllCupsMask) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(standingCupMask));
            }

            previousPosition = launchPosition;
            position = launchPosition;
            velocity = initialVelocity;
            activeCupMask = standingCupMask;
            tableBounceCount = 0;
            rimBounceCount = 0;
            lastRimCupIndex = -1;
            lastRimCollisionTime = float.NegativeInfinity;
            elapsedTime = 0f;
            settledTime = 0f;
            isGrounded = false;
            accumulator = 0d;
            result = default;
            status = BeerPongBallStatus.InFlight;
        }

        public Vector3 LaunchFromAim(
            float yawDegrees,
            float pitchDegrees,
            float power,
            int standingCupMask)
        {
            Vector3 initialVelocity = BeerPongAim.ToVelocity(
                yawDegrees,
                pitchDegrees,
                power);
            Launch(layout.ThrowOrigin, initialVelocity, standingCupMask);
            return initialVelocity;
        }

        public int Advance(float renderDeltaTime)
        {
            if (!BeerPongMath.IsFiniteNonNegative(renderDeltaTime))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(renderDeltaTime));
            }

            if (!IsInFlight || renderDeltaTime <= 0f)
            {
                return 0;
            }

            accumulator += renderDeltaTime;
            int steps = 0;
            while (IsInFlight &&
                   accumulator + AccumulatorEpsilon >= FixedStepSeconds)
            {
                StepFixed();
                accumulator = Math.Max(
                    0d,
                    accumulator - FixedStepSeconds);
                steps++;
            }

            if (!IsInFlight)
            {
                accumulator = 0d;
            }

            return steps;
        }

        public BeerPongBallSnapshot StepFixed()
        {
            if (!IsInFlight)
            {
                throw new InvalidOperationException(
                    "Launch a ball before stepping the simulation.");
            }

            previousPosition = position;
            elapsedTime += FixedDeltaTime;
            if (isGrounded)
            {
                SimulateGrounded();
            }
            else
            {
                SimulateAirborne();
            }

            if (IsInFlight)
            {
                ResolveTerminalConditions();
            }

            return Snapshot;
        }

        public bool TryGetResult(out BeerPongFlightResult flightResult)
        {
            flightResult = result;
            return IsComplete;
        }

        public void Reset()
        {
            status = BeerPongBallStatus.Ready;
            result = default;
            previousPosition = layout.ThrowOrigin;
            position = layout.ThrowOrigin;
            velocity = Vector3.zero;
            activeCupMask = 0;
            tableBounceCount = 0;
            rimBounceCount = 0;
            lastRimCupIndex = -1;
            lastRimCollisionTime = float.NegativeInfinity;
            elapsedTime = 0f;
            settledTime = 0f;
            isGrounded = false;
            accumulator = 0d;
        }

        private void SimulateAirborne()
        {
            velocity.y += settings.Gravity * FixedDeltaTime;
            float dragRetention =
                1f / (1f + settings.AirDrag * FixedDeltaTime);
            velocity *= dragRetention;

            Vector3 start = position;
            Vector3 end = start + velocity * FixedDeltaTime;
            if (TryResolveCupInteraction(start, ref end))
            {
                return;
            }

            ResolveSweptTableCollision(start, ref end);
            position = end;
        }

        private void SimulateGrounded()
        {
            float horizontalSpeed = new Vector2(
                velocity.x,
                velocity.z).magnitude;
            float reducedSpeed = Mathf.Max(
                0f,
                horizontalSpeed - settings.GroundFriction * FixedDeltaTime);
            float scale = horizontalSpeed > 0.000001f
                ? reducedSpeed / horizontalSpeed
                : 0f;
            velocity = new Vector3(
                velocity.x * scale,
                0f,
                velocity.z * scale);

            Vector3 next = position + velocity * FixedDeltaTime;
            next.y = layout.TableSurfaceY + layout.BallRadius;
            position = next;
            if (!layout.IsPointOverTable(position))
            {
                isGrounded = false;
                settledTime = 0f;
                return;
            }

            if (reducedSpeed <= settings.SettledHorizontalSpeed)
            {
                settledTime += FixedDeltaTime;
                if (settledTime >= settings.SettledDuration)
                {
                    CompleteMiss(BeerPongMissReason.Settled);
                }
            }
            else
            {
                settledTime = 0f;
            }
        }

        private bool TryResolveCupInteraction(
            Vector3 start,
            ref Vector3 end)
        {
            float deltaY = end.y - start.y;
            if (Mathf.Abs(deltaY) <= 0.000001f)
            {
                return false;
            }

            bool downward = deltaY < 0f;
            for (int cupIndex = 0;
                 cupIndex < BeerPongTableLayout.CupCount;
                 cupIndex++)
            {
                if (!layout.IsCupActive(activeCupMask, cupIndex))
                {
                    continue;
                }

                BeerPongCupDefinition cup = layout.GetCup(cupIndex);
                float crossing = (cup.MouthCenter.y - start.y) / deltaY;
                if (crossing < 0f || crossing > 1f)
                {
                    continue;
                }

                Vector3 contact = Vector3.Lerp(start, end, crossing);
                Vector2 radial = new Vector2(
                    contact.x - cup.MouthCenter.x,
                    contact.z - cup.MouthCenter.z);
                float radialDistance = radial.magnitude;
                float captureRadius = Mathf.Max(
                    0.01f,
                    cup.MouthRadius - layout.BallRadius * 0.65f);

                if (downward && radialDistance <= captureRadius)
                {
                    position = contact;
                    CompleteSink(cupIndex);
                    return true;
                }

                float rimOuterRadius =
                    cup.MouthRadius + layout.BallRadius;
                if (radialDistance > rimOuterRadius ||
                    radialDistance < captureRadius ||
                    IsRimOnCooldown(cupIndex))
                {
                    continue;
                }

                Vector3 horizontalNormal;
                if (radialDistance <= 0.000001f)
                {
                    horizontalNormal = Vector3.right;
                }
                else
                {
                    float direction =
                        radialDistance >= cup.MouthRadius ? 1f : -1f;
                    horizontalNormal = new Vector3(
                        radial.x / radialDistance * direction,
                        0f,
                        radial.y / radialDistance * direction);
                }

                Vector3 verticalNormal =
                    downward ? Vector3.up : Vector3.down;
                Vector3 normal = (
                    horizontalNormal +
                    verticalNormal * RimVerticalNormalWeight).normalized;
                if (Vector3.Dot(velocity, normal) >= 0f)
                {
                    normal = -normal;
                }

                velocity =
                    Vector3.Reflect(velocity, normal) *
                    settings.RimRestitution;
                float remainingTime =
                    FixedDeltaTime * Mathf.Clamp01(1f - crossing);
                end =
                    contact +
                    normal * 0.002f +
                    velocity * remainingTime;
                rimBounceCount++;
                lastRimCupIndex = cupIndex;
                lastRimCollisionTime = elapsedTime;
                settledTime = 0f;
                isGrounded = false;
                return false;
            }

            return false;
        }

        private bool IsRimOnCooldown(int cupIndex)
        {
            return
                lastRimCupIndex == cupIndex &&
                elapsedTime - lastRimCollisionTime <
                RimCollisionCooldown;
        }

        private void ResolveSweptTableCollision(
            Vector3 start,
            ref Vector3 end)
        {
            if (velocity.y >= 0f)
            {
                return;
            }

            float ballSurfaceY =
                layout.TableSurfaceY + layout.BallRadius;
            float deltaY = end.y - start.y;
            if (start.y < ballSurfaceY ||
                end.y > ballSurfaceY ||
                Mathf.Abs(deltaY) <= 0.000001f)
            {
                return;
            }

            float crossing = (ballSurfaceY - start.y) / deltaY;
            if (crossing < 0f || crossing > 1f)
            {
                return;
            }

            Vector3 contact = Vector3.Lerp(start, end, crossing);
            if (!layout.IsPointOverTable(contact))
            {
                return;
            }

            tableBounceCount++;
            velocity = new Vector3(
                velocity.x * settings.TableTangentialRetention,
                -velocity.y * settings.TableRestitution,
                velocity.z * settings.TableTangentialRetention);
            float remainingTime =
                FixedDeltaTime * Mathf.Clamp01(1f - crossing);
            if (velocity.y < settings.MinimumVerticalBounceSpeed)
            {
                velocity.y = 0f;
                isGrounded = true;
                end = contact + velocity * remainingTime;
                end.y = ballSurfaceY;
            }
            else
            {
                end = contact + velocity * remainingTime;
            }

            settledTime = 0f;
        }

        private void ResolveTerminalConditions()
        {
            float margin = settings.OutOfBoundsMargin;
            if (position.x < -layout.TableHalfWidth - margin ||
                position.x > layout.TableHalfWidth + margin ||
                position.z < layout.TableNearZ - margin ||
                position.z > layout.TableFarZ + margin ||
                position.y < layout.TableSurfaceY - margin ||
                position.y >
                layout.TableSurfaceY + settings.MaximumHeight)
            {
                CompleteMiss(BeerPongMissReason.OutOfBounds);
                return;
            }

            if (elapsedTime >= settings.MaxFlightDuration)
            {
                CompleteMiss(BeerPongMissReason.Timeout);
            }
        }

        private void CompleteSink(int cupIndex)
        {
            status = BeerPongBallStatus.Sunk;
            result = BeerPongFlightResult.CreateSink(
                cupIndex,
                tableBounceCount > 0,
                elapsedTime,
                tableBounceCount,
                rimBounceCount,
                position,
                velocity);
        }

        private void CompleteMiss(BeerPongMissReason reason)
        {
            status = BeerPongBallStatus.Missed;
            result = BeerPongFlightResult.CreateMiss(
                reason,
                elapsedTime,
                tableBounceCount,
                rimBounceCount,
                position,
                velocity);
        }
    }
}
