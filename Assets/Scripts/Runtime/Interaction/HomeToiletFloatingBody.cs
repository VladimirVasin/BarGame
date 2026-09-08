using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Scoped rigid-body integration for the authored solid inside the toilet's
    /// visual cavity. The player's solid fixture collider is intentionally not
    /// part of this simulation. State changes only through Advance, never Time.
    /// </summary>
    public sealed class HomeToiletFloatingBody
    {
        public const float FixedStep = 1f / 120f;
        public const float Length = .08f;
        // Includes the authored centre-line bend and irregular radial facets.
        public const float Radius = .024f;
        public const float LensClearance = .026f;
        private const float HalfSegment = Length * .5f - Radius;
        private const float Floor = .28f - .4373f;
        private const float RelativeDensity = .76f;
        private const float Gravity = 9.81f;
        private const float LinearDrag = 9f;
        private const float TransverseInertia = Length * Length / 12f + Radius * Radius / 4f;
        private const float AxialInertia = Radius * Radius * .5f;
        private Vector3 waterOrigin, cameraPosition;
        private Quaternion bowlRotation, inverseBowlRotation;
        private double remainder;
        private float elapsed, flushElapsed;
        private bool flushing;

        public bool IsActive { get; private set; }
        public bool IsDrained { get; private set; }
        /// <summary>World-space centre of mass; the FBX root is its lower tip.</summary>
        public Vector3 Position { get; private set; }
        public Vector3 Velocity { get; private set; }
        /// <summary>World-space radians per second.</summary>
        public Vector3 AngularVelocity { get; private set; }
        public Quaternion Rotation { get; private set; } = Quaternion.identity;
        public Vector3 TipPosition => Position - Rotation * Vector3.up * (Length * .5f);
        public float SubmergedFraction { get; private set; }

        public void Begin(Vector3 waterCentre, Quaternion roomRotation, Vector3 tip,
            Quaternion orientation, Vector3 velocity, Vector3 angularVelocity, Vector3 camera)
        {
            Reset();
            waterOrigin = waterCentre;
            bowlRotation = roomRotation;
            inverseBowlRotation = Quaternion.Inverse(roomRotation);
            Rotation = orientation;
            Position = tip + orientation * Vector3.up * (Length * .5f);
            Velocity = velocity;
            AngularVelocity = angularVelocity;
            cameraPosition = camera;
            IsActive = true;
            Constrain();
        }

        public void SetCamera(Vector3 position) => cameraPosition = position;
        public void BeginFlush() { if (!flushing) { flushing = true; flushElapsed = 0f; } }

        public void Advance(float seconds, float flushStrength = 0f)
        {
            if (!IsActive || IsDrained || seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            remainder += seconds;
            while (remainder + 1e-9 >= FixedStep && !IsDrained)
            {
                Step(FixedStep, Mathf.Clamp01(flushStrength));
                remainder = System.Math.Max(0, remainder - FixedStep);
            }
        }

        private void Step(float dt, float flushStrength)
        {
            elapsed += dt;
            if (flushing) flushElapsed += dt;
            Vector3 up = bowlRotation * Vector3.up;
            Vector3 acceleration = -up * Gravity;
            Vector3 torque = Vector3.zero;
            float submerged = 0f;
            // Five displaced volumes supply buoyancy and drag at different
            // lever arms. Partial immersion therefore produces righting/rolling
            // moments instead of assigning a horizontal target rotation.
            for (int index = 0; index < 5; index++)
            {
                float along = (index - 2) * HalfSegment * .5f;
                Vector3 arm = Rotation * Vector3.up * along;
                Vector3 local = inverseBowlRotation * (Position + arm - waterOrigin);
                float height = .0012f * Noise(elapsed * .83f + local.x * 2f, 13);
                float cap = Mathf.Clamp((height - local.y + Radius) / (2f * Radius), 0f, 1f);
                float fraction = cap * cap * (3f - 2f * cap);
                submerged += fraction * .2f;
                Vector3 fluid = bowlRotation * Current(local, flushStrength);
                Vector3 pointVelocity = Velocity + Vector3.Cross(AngularVelocity, arm);
                Vector3 force = (up * (Gravity / RelativeDensity) +
                    (fluid - pointVelocity) * LinearDrag) * (fraction * .2f);
                acceleration += force;
                torque += Vector3.Cross(arm, force);
            }
            SubmergedFraction = submerged;
            acceleration -= Velocity * (.12f * (1f - submerged));
            Vector3 bodyTorque = Quaternion.Inverse(Rotation) * torque;
            Vector3 angularAcceleration = Rotation * new Vector3(bodyTorque.x / TransverseInertia,
                bodyTorque.y / AxialInertia, bodyTorque.z / TransverseInertia);
            // A changing fluid curl applies angular drag to the irregular prop.
            // Noise drives water velocity, never the object's pose or position.
            Vector3 fluidSpin = bowlRotation * new Vector3(.55f * Noise(elapsed * .71f, 41),
                .32f + .5f * Noise(elapsed * .63f, 73) + 8f * flushStrength,
                .65f * Noise(elapsed * .91f, 107));
            angularAcceleration += (fluidSpin - AngularVelocity) * (2.6f * submerged + .1f);
            AngularVelocity += Vector3.ClampMagnitude(angularAcceleration, 100f) * dt;
            AngularVelocity = Vector3.ClampMagnitude(AngularVelocity, 14f);
            Velocity = Vector3.ClampMagnitude(Velocity + acceleration * dt, 1.2f);
            Position += Velocity * dt;
            float speed = AngularVelocity.magnitude;
            if (speed > .00001f)
                Rotation = Quaternion.AngleAxis(speed * dt * Mathf.Rad2Deg, AngularVelocity / speed) * Rotation;
            Constrain();
            Vector3 centre = inverseBowlRotation * (Position - waterOrigin);
            if (flushing && flushElapsed > .75f && centre.y < Floor + .032f &&
                new Vector2(centre.x, centre.z).magnitude < .055f)
            {
                // Retire fully submerged inside the lower cavity; never push
                // the imported mesh through the closed ceramic floor.
                IsDrained = true;
                Velocity = AngularVelocity = Vector3.zero;
            }
        }

        private Vector3 Current(Vector3 local, float strength)
        {
            float drain = flushing ? Mathf.SmoothStep(0f, 1f, flushElapsed / .35f) * strength : 0f;
            Vector3 radial = new Vector3(local.x, 0f, local.z);
            Vector3 tangent = Vector3.Cross(Vector3.up, radial);
            return new Vector3(.018f * Noise(elapsed * .87f + local.z * 3f, 7),
                -.95f * drain, .023f * Noise(elapsed * .73f + local.x * 3f, 29)) +
                tangent * (.32f + 8f * drain) - radial * (10f * drain);
        }

        private void Constrain()
        {
            // The bowl is a 24-sided elliptical loft. These three ring samples
            // match the validated Blender inner wall at .28/.32/.4373 metres;
            // the cosine inset stays inside its flat facets, not outside them.
            for (int pass = 0; pass < 8; pass++)
            {
                for (int index = 0; index < 5; index++)
                {
                    Vector3 arm = Rotation * Vector3.up * ((index - 2) * HalfSegment * .5f);
                    Vector3 local = inverseBowlRotation * (Position + arm - waterOrigin);
                    if (local.y < Floor + Radius)
                    {
                        ResolveContact(bowlRotation * Vector3.down, Floor + Radius - local.y, arm);
                        local = inverseBowlRotation * (Position + arm - waterOrigin);
                    }
                    Radii(local.y, out Vector2 radii, out Vector2 slope);
                    float q = Mathf.Sqrt(local.x * local.x / (radii.x * radii.x) +
                        local.z * local.z / (radii.y * radii.y));
                    if (q < .00001f) continue;
                    Vector3 gradient = new Vector3(local.x / (q * radii.x * radii.x),
                        -(local.x * local.x * slope.x / (radii.x * radii.x * radii.x) +
                        local.z * local.z * slope.y / (radii.y * radii.y * radii.y)) / q,
                        local.z / (q * radii.y * radii.y));
                    float distance = (q - 1f) / gradient.magnitude;
                    if (distance > -Radius)
                        ResolveContact(bowlRotation * gradient.normalized, distance + Radius, arm);
                }
                Vector3 axis = Rotation * Vector3.up;
                float along = Mathf.Clamp(Vector3.Dot(cameraPosition - Position, axis), -HalfSegment, HalfSegment);
                Vector3 cameraArm = axis * along;
                Vector3 difference = Position + cameraArm - cameraPosition;
                float distanceToLens = difference.magnitude;
                if (distanceToLens < Radius + LensClearance)
                {
                    Vector3 away = distanceToLens > .00001f ? difference / distanceToLens : bowlRotation * Vector3.forward;
                    ResolveContact(-away, Radius + LensClearance - distanceToLens, cameraArm);
                }
            }
        }

        private void ResolveContact(Vector3 outward, float depth, Vector3 arm)
        {
            Position -= outward * depth;
            Vector3 pointVelocity = Velocity + Vector3.Cross(AngularVelocity, arm);
            float closing = Vector3.Dot(pointVelocity, outward);
            if (closing <= 0f) return;
            Vector3 cross = Vector3.Cross(arm, outward);
            Vector3 bodyCross = Quaternion.Inverse(Rotation) * cross;
            Vector3 inverseMoment = Rotation * new Vector3(bodyCross.x / TransverseInertia,
                bodyCross.y / AxialInertia, bodyCross.z / TransverseInertia);
            float impulse = closing * 1.08f / (1f + Vector3.Dot(cross, inverseMoment));
            Velocity -= outward * impulse;
            AngularVelocity -= inverseMoment * impulse;
        }

        private static void Radii(float y, out Vector2 radii, out Vector2 slope)
        {
            Vector2 bottom = new Vector2(.050f, .045f), middle = new Vector2(.118f, .105f), top = new Vector2(.170f, .157f);
            const float middleY = .32f - .4373f;
            if (y < middleY)
            {
                radii = Vector2.Lerp(bottom, middle, Mathf.InverseLerp(Floor, middleY, y));
                slope = y > Floor ? (middle - bottom) / (middleY - Floor) : Vector2.zero;
            }
            else
            {
                radii = Vector2.Lerp(middle, top, Mathf.InverseLerp(middleY, 0f, y));
                slope = y < 0f ? (top - middle) / -middleY : Vector2.zero;
            }
            const float facetInset = .9914449f; // cos(pi / 24)
            radii *= facetInset;
            slope *= facetInset;
        }

        private static float Noise(float t, uint salt)
        {
            int tick = Mathf.FloorToInt(t);
            float u = t - tick;
            return Mathf.Lerp(Hash(tick, salt), Hash(tick + 1, salt), u * u * (3f - 2f * u));
        }

        private static float Hash(int tick, uint salt)
        {
            unchecked
            {
                uint value = (uint)tick * 747796405u + salt * 2891336453u;
                value = ((value >> ((int)(value >> 28) + 4)) ^ value) * 277803737u;
                value = (value >> 22) ^ value;
                return (value & 0x00ffffff) / 8388607.5f - 1f;
            }
        }

        public void Reset()
        {
            IsActive = IsDrained = flushing = false;
            Position = Velocity = AngularVelocity = Vector3.zero;
            Rotation = Quaternion.identity;
            SubmergedFraction = elapsed = flushElapsed = 0f;
            remainder = 0;
        }
    }
}
