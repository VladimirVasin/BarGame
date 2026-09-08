using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        /// <summary>
        /// The park landmark, photographed from the ground.
        ///
        /// The bandstand shipped for a fortnight with its four columns and
        /// its balustrade standing beside the deck in mid air, and its
        /// pennant hanging a quarter of its span past the eave. Nothing
        /// caught it: the misc catalog checks a merged assembly's lowest
        /// point, which the stone plinth satisfies on its own, and the wind
        /// validator never asks where a cloth hangs. It went out because the
        /// 2026-08-26 migration replaced the visual and no one looked at it.
        ///
        /// So this asks the two questions a picture answers: is every raised
        /// piece over the deck, and is the pennant under the eave.
        /// </summary>
        [UnityTest]
        [Explicit("Focused park bandstand captures: columns, balustrade and pennant over the deck.")]
        public IEnumerator CityParkBandstand()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime(360f);

            CityGameRoot city = null;
            yield return Capture(SceneIds.City, () =>
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                return city != null && city.IsInitialized ? city : null;
            }, () =>
            {
                var stand = default(CityDecorationDescriptor);
                bool found = false;
                IReadOnlyList<CityDecorationDescriptor> descriptors =
                    city.World.DecorationPlan.Descriptors;
                for (int index = 0; index < descriptors.Count; index++)
                {
                    if (descriptors[index].Kind !=
                        CityDecorationKind.ParkBandstand)
                    {
                        continue;
                    }

                    stand = descriptors[index];
                    found = true;
                    break;
                }

                Assert.That(found, Is.True,
                    "The enabled park owes the city one bandstand.");

                // The frame the mesh and the pennant both resolve through:
                // the descriptor's forward snapped to a cardinal axis, and
                // the tangent taken from it the way the planners take it.
                Vector3 raw = stand.Forward;
                Vector3 forward = Mathf.Abs(raw.x) > Mathf.Abs(raw.z)
                    ? new Vector3(Mathf.Sign(raw.x), 0f, 0f)
                    : new Vector3(0f, 0f, Mathf.Sign(raw.z));
                Vector3 tangent = new Vector3(-forward.z, 0f, forward.x);
                Vector3 origin = stand.Position;

                AssertBandstandPennantHangsUnderItsEave(city, origin,
                    forward, tangent);
                AssertBandstandProxiesStayInsideItsPlinth(city, stand,
                    origin, forward, tangent);

                city.Player.Motor.SetInputEnabled(false);
                city.Player.Motor.Teleport(origin - (forward * 11f));

                Vector3 eye = Vector3.up * EyeHeight;
                Vector3 deck = origin + (Vector3.up * 0.54f);
                return new[]
                {
                    // Square on the railed quarter behind the bandstand,
                    // where the rails used to run out past the deck edge on
                    // both hands: the columns must stand on boards.
                    Shot.At("bandstand-00-railed-side",
                        origin - (forward * 10.5f) + eye,
                        origin + (Vector3.up * 2.6f), 60f),
                    // From the park, across the open half, with the
                    // balustrade read through the structure.
                    Shot.At("bandstand-01-from-the-park",
                        origin + (forward * 9.0f) + (tangent * 4.5f) + eye,
                        deck, 58f),
                    // Low and close: a column foot against the deck edge.
                    Shot.At("bandstand-02-column-foot",
                        origin + (forward * 5.2f) + (tangent * 5.2f) +
                            (Vector3.up * 0.9f),
                        origin + (Vector3.up * 1.1f), 52f),
                    // Up into the eave, which is what carries the pennant.
                    Shot.At("bandstand-03-under-the-eave",
                        origin + (tangent * 1.2f) + (Vector3.up * 1.5f),
                        origin + (Vector3.up * 4.6f), 70f),
                };
            });

            Debug.Log(
                "PARK BANDSTAND ACCEPTANCE OK: pennant under the eave, " +
                "frames written for the columns, balustrade and deck.");
        }

        /// <summary>
        /// The eave underside at `4.00` is a twelve-sided ring `3.68` across
        /// the tangent and `3.08` along forward. A pennant pinned outside it
        /// hangs from nothing, which is what the rectangle it used to be read
        /// from allowed.
        ///
        /// Measured against the ring itself, not the ellipse it is inscribed
        /// in: an ellipse test is slack by `cos^2 15` at the facet midpoints,
        /// so a pin up to `0.12 m` past the roof passes it. Substituting the
        /// easy shape for the real one is the whole defect this guards
        /// against, and the guard must not repeat it. The cloth is checked
        /// rather than the pin, because the panel is `0.28` wide and centred
        /// on it, so its outer corner is what reaches furthest out.
        /// </summary>
        private static void AssertBandstandPennantHangsUnderItsEave(
            CityGameRoot city,
            Vector3 origin,
            Vector3 forward,
            Vector3 tangent)
        {
            CityWindDressingPlan wind = city.World.WindDressingPlan;
            Assert.That(wind, Is.Not.Null, "The park owes the city its wind dressing.");

            var pennant = default(CityWindDressingClothDescriptor);
            bool found = false;
            for (int index = 0; index < wind.Cloths.Count; index++)
            {
                if (wind.Cloths[index].Kind !=
                    CityWindDressingKind.BandstandPennant)
                {
                    continue;
                }

                pennant = wind.Cloths[index];
                found = true;
                break;
            }

            Assert.That(found, Is.True,
                "The bandstand owns the park's one remnant pennant.");

            Vector3 offset = pennant.Position - origin;
            float across = Vector3.Dot(offset, tangent);
            float along = Vector3.Dot(offset, forward);
            float half = pennant.Width * 0.5f;
            foreach (float edge in new[] { across - half, across, across + half })
            {
                float seated = EaveRingFraction(edge, along);
                Assert.That(seated, Is.LessThan(1f),
                    $"The pennant reaches {seated:F3} of the way out of an " +
                    "eave that ends at 1.000; it hangs past the roof, in " +
                    "the air.");
            }

            Assert.That(offset.y, Is.InRange(3.9f, 4.1f),
                "The pennant pins to the eave underside.");
        }

        /// <summary>
        /// What the hero is stopped by has to be the shape he can see. The
        /// blocking proxy was the platform's pre-migration rectangle long
        /// after the platform became a twelve-gon `3.72` across the tangent
        /// and `3.12` along forward, so it walled off four corners that hold
        /// nothing and let him into the stone where it is widest. Every
        /// proxy corner is now required to sit on or inside that ring.
        /// </summary>
        private static void AssertBandstandProxiesStayInsideItsPlinth(
            CityGameRoot city,
            CityDecorationDescriptor stand,
            Vector3 origin,
            Vector3 forward,
            Vector3 tangent)
        {
            var proxies = new List<Bounds>();
            CityStaticCollisionBuilder.AddDecorationProxyBounds(
                city.Layout, stand, proxies);
            Assert.That(proxies, Is.Not.Empty,
                "The bandstand is a blocking landmark.");

            float widest = 0f;
            foreach (Bounds proxy in proxies)
            {
                Vector3 offset = proxy.center - origin;
                float acrossCentre = Vector3.Dot(offset, tangent);
                float alongCentre = Vector3.Dot(offset, forward);
                float acrossHalf = Mathf.Abs(
                    Vector3.Dot(proxy.extents, tangent));
                float alongHalf = Mathf.Abs(
                    Vector3.Dot(proxy.extents, forward));
                foreach (int acrossSign in new[] { -1, 1 })
                {
                    foreach (int alongSign in new[] { -1, 1 })
                    {
                        float seated = PlinthRingFraction(
                            acrossCentre + (acrossSign * acrossHalf),
                            alongCentre + (alongSign * alongHalf));
                        widest = Mathf.Max(widest, seated);
                        Assert.That(seated, Is.LessThan(1.001f),
                            $"A blocking corner reaches {seated:F3} of the " +
                            "way out of a plinth that ends at 1.000: it is " +
                            "an invisible wall beside the bandstand.");
                    }
                }
            }

            Debug.Log(
                $"BANDSTAND PROXIES: {proxies.Count} boxes, " +
                $"furthest corner at {widest:F3} of the plinth.");
        }

        private static float PlinthRingFraction(float across, float along)
        {
            return RingFraction(across, along, 3.72f, 3.12f);
        }

        /// <summary>
        /// How far out a point lies on the eave's twelve-gon, where `1.0` is
        /// its boundary: the distance to the point over the distance from the
        /// centre to the ring's edge in the same direction.
        /// </summary>
        private static float EaveRingFraction(float across, float along)
        {
            return RingFraction(across, along, 3.68f, 3.08f);
        }

        private static float RingFraction(
            float across,
            float along,
            float Across,
            float Along)
        {
            const int Sides = 12;

            float distance = Mathf.Sqrt((across * across) + (along * along));
            if (distance <= Mathf.Epsilon)
            {
                return 0f;
            }

            float angle = Mathf.Atan2(along, across);
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            for (int side = 0; side < Sides; side++)
            {
                float first = side * Mathf.PI * 2f / Sides;
                float second = (side + 1) * Mathf.PI * 2f / Sides;
                var a = new Vector2(
                    Across * Mathf.Cos(first), Along * Mathf.Sin(first));
                var b = new Vector2(
                    Across * Mathf.Cos(second), Along * Mathf.Sin(second));
                Vector2 edge = b - a;
                float denominator =
                    (edge.x * direction.y) - (edge.y * direction.x);
                if (Mathf.Abs(denominator) < 1e-9f)
                {
                    continue;
                }

                float t =
                    ((a.y * direction.x) - (a.x * direction.y)) / denominator;
                if (t < -1e-6f || t > 1f + 1e-6f)
                {
                    continue;
                }

                Vector2 hit = a + (edge * t);
                if (Vector2.Dot(hit, direction) > 0f)
                {
                    return distance / hit.magnitude;
                }
            }

            throw new InvalidOperationException(
                "The eave ring has no edge in that direction.");
        }
    }
}
