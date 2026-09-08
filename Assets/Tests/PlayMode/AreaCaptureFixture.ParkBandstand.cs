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

                city.Player.Motor.SetInputEnabled(false);
                city.Player.Motor.Teleport(origin - (forward * 11f));

                Vector3 eye = Vector3.up * EyeHeight;
                Vector3 deck = origin + (Vector3.up * 0.54f);
                return new[]
                {
                    // Along the open side: the columns must stand on boards,
                    // not beside them.
                    Shot.At("bandstand-00-open-side",
                        origin - (forward * 10.5f) + eye,
                        origin + (Vector3.up * 2.6f), 60f),
                    // Across the railed side, where the balustrade used to
                    // run out past the deck edge on both hands.
                    Shot.At("bandstand-01-balustrade",
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
        /// the tangent and `3.08` along forward. A pennant pinned outside
        /// that ellipse hangs from nothing, which is what the rectangle it
        /// used to be read from allowed.
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
            float seated = ((across / 3.68f) * (across / 3.68f)) +
                           ((along / 3.08f) * (along / 3.08f));
            Assert.That(seated, Is.LessThan(1f),
                $"The pennant hangs {seated:F3} of the way out of an eave " +
                "that ends at 1.000; it is pinned past the roof, in the air.");
            Assert.That(offset.y, Is.InRange(3.9f, 4.1f),
                "The pennant pins to the eave underside.");
        }
    }
}
