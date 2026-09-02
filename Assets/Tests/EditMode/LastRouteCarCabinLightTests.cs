using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The light INSIDE the Ferryman's car, which until 2026-09-02 did not
    /// exist: the car carried three lights and all three pointed out of it,
    /// so the cabin they burned from was the one place on the whole journey
    /// nothing lit. The user rode up through the forest and reported the
    /// obvious - "просто в черноте".
    ///
    /// A whole file for three fixtures, on the precedent of
    /// <c>MountainRoadApronFloodlightTests</c>: a light nobody asserts is a
    /// light that drifts, and two of the numbers in here are the only thing
    /// standing between this feature and a milky veil across the entire
    /// first-person ride.
    /// </summary>
    public sealed class LastRouteCarCabinLightTests
    {
        private static LastRouteCarAssetRegistry BuildCar(
            out GameObject parent,
            out Transform root)
        {
            parent = new GameObject("Cabin Light Test");
            LastRouteCarAssetRegistry car = LastRouteCarFactory.Create(
                parent.transform,
                LastRouteCarPlan.At(Vector3.zero, Vector3.forward));
            Assert.That(car, Is.Not.Null, "The car failed to spawn.");
            root = car.transform.parent != null
                ? car.transform.parent
                : car.transform;
            return car;
        }

        private static LastRouteCarCabinLight Occupy(Transform root)
        {
            var cabin = root.GetComponent<LastRouteCarCabinLight>();
            Assert.That(
                cabin,
                Is.Not.Null,
                "The car was built without its cabin lighting.");
            Assert.That(
                cabin.IsInitialized,
                Is.True,
                "The cabin lighting never bound its drawn surfaces, which " +
                "means the prefab is stale - regenerate the model AND " +
                "rebuild the prefab, or this feature silently does not " +
                "exist.");
            cabin.ForceOccupiedForTests(true);
            return cabin;
        }

        /// <summary>
        /// The gate is OCCUPANCY, and this is the half of it that keeps the
        /// island's twelve-light budget intact: a parked car nobody is in
        /// raises no Light at all. Its lit LENS still burns - that is the
        /// §20 half, asserted below - which is what makes a parked car read
        /// as a car somebody is waiting in.
        /// </summary>
        [Test]
        public void ParkedCar_CarriesNoLampUntilSomebodyIsInIt()
        {
            LastRouteCarAssetRegistry car = BuildCar(
                out GameObject parent,
                out Transform root);
            try
            {
                Assert.That(car, Is.Not.Null);
                var cabin = root.GetComponent<LastRouteCarCabinLight>();
                Assert.That(cabin, Is.Not.Null);
                Assert.That(cabin.IsOccupied, Is.False);
                Assert.That(
                    root.GetComponentsInChildren<Light>(true),
                    Is.Empty,
                    "An empty parked car is spending a realtime light on a " +
                    "cabin nobody is sitting in.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        /// <summary>
        /// Where the lamps hang and which way they face, both measured
        /// against the RUNTIME ROOT.
        ///
        /// This is the assertion that would catch the black-scene bug for a
        /// NEW light. The carrier is the sprung body, whose `localRotation`
        /// is copied straight off an imported node whose forward is very
        /// nearly vertical; aiming with a local Euler in that space threw
        /// both headlight beams at the sky and shipped a black mountain. The
        /// existing forward-dot tests cover `LeftBeam`, `RightBeam` and
        /// `Spill` by name and would not have noticed a fourth lamp doing
        /// it.
        /// </summary>
        [Test]
        public void CabinLamp_HangsOffTheSprungBodyAndRakesDownFromTheRoot()
        {
            BuildCar(out GameObject parent, out Transform root);
            try
            {
                LastRouteCarCabinLight cabin = Occupy(root);
                var suspension = root.GetComponent<LastRouteCarSuspension>();
                Assert.That(
                    cabin.CabinLamp,
                    Is.Not.Null,
                    "An occupied cabin has no plafond in it.");
                Assert.That(
                    cabin.GloveboxLamp,
                    Is.Not.Null,
                    "The glovebox has no bulb.");
                Assert.That(
                    cabin.CabinLamp.type,
                    Is.EqualTo(LightType.Spot));
                Assert.That(
                    cabin.GloveboxLamp.type,
                    Is.EqualTo(LightType.Point),
                    "A bulb in a box has no direction to point.");

                if (suspension != null && suspension.SprungBody != null)
                {
                    Assert.That(
                        cabin.CabinLamp.transform.parent,
                        Is.EqualTo(suspension.SprungBody),
                        "The plafond must ride the sprung body, so it dips " +
                        "with the cabin it is lighting.");
                }

                float rake = Vector3.Dot(
                    cabin.CabinLamp.transform.forward,
                    root.up);
                Assert.That(
                    rake,
                    Is.LessThan(-0.55f),
                    $"The plafond rakes {rake:0.00} against the car's own " +
                    "up. A lamp aimed with a local Euler on this carrier " +
                    "points at the sky - that is exactly how the headlights " +
                    "once shipped a black scene.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        /// <summary>
        /// THE assertion this whole design rests on.
        ///
        /// The windscreen is built as a double quad, so there is a real
        /// inward-facing pane a hand's breadth in front of the sitters, and
        /// `Glass` has its ShadowCaster pass disabled - shadows would not
        /// contain this lamp, they would let it out at the bonnet. The only
        /// thing keeping the pane dark is the cone: with the axis tilted
        /// back at the seats the pane sits about `81°` off it and the bonnet
        /// about `85°`, both outside the `70°` outer half-angle, both
        /// receiving exactly nothing. Widen this and a milky veil comes back
        /// across the entire two-minute ride, and nothing else in the suite
        /// would notice.
        /// </summary>
        [Test]
        public void CabinLamp_KeepsTheWindscreenAndTheBonnetOutOfItsCone()
        {
            Assert.That(
                LastRouteCarCabinLight.CabinLampSpotAngle,
                Is.LessThanOrEqualTo(145f),
                "The cone has been widened past the point where the " +
                "windscreen's inner pane (81 deg off the axis) and the " +
                "bonnet (85 deg) stay outside it.");
            Assert.That(
                LastRouteCarCabinLight.CabinLampInnerSpotAngle,
                Is.LessThan(LastRouteCarCabinLight.CabinLampSpotAngle),
                "A spot's core cannot be wider than its cone.");
            Assert.That(
                LastRouteCarCabinLight.CabinLampInnerSpotAngle,
                Is.GreaterThanOrEqualTo(100f),
                "The core has narrowed off the dashboard, which sits " +
                "41-56 deg off the axis and is the surface the user asked " +
                "to be able to read.");
        }

        /// <summary>
        /// Neither lamp can put a pool on the road, and it is arithmetic
        /// rather than art direction: URP's range fade is
        /// `saturate(1 - (d²/r²)²)²`, which is EXACTLY zero at the range. A
        /// lamp whose range is shorter than its own height above the ground
        /// cannot reach it at any intensity.
        /// </summary>
        [Test]
        public void CabinLamps_CannotReachTheRoad()
        {
            BuildCar(out GameObject parent, out Transform root);
            try
            {
                LastRouteCarCabinLight cabin = Occupy(root);
                foreach (Light lamp in new[]
                         {
                             cabin.CabinLamp,
                             cabin.GloveboxLamp
                         })
                {
                    Assert.That(lamp, Is.Not.Null);
                    float height = Vector3.Dot(
                        lamp.transform.position - root.position,
                        root.up);
                    Assert.That(
                        lamp.range,
                        Is.LessThan(height),
                        $"{lamp.name} reaches {lamp.range:0.00} m from " +
                        $"{height:0.00} m up, so it lays a pool of cabin " +
                        "light on the road under the car.");
                }
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        /// <summary>
        /// The cabin's own scale, which is neither of this game's exterior
        /// bands. The mountain runs `1.65`-`18` and the city `31`-`240`,
        /// both sized for fixtures throwing metres; this one crosses half a
        /// metre to a face, and the glovebox bulb nine centimetres.
        /// </summary>
        [Test]
        public void CabinLamps_AreOnTheCabinsOwnScale()
        {
            Assert.That(
                LastRouteCarCabinLight.CabinLampIntensity,
                Is.InRange(0.8f, 3.0f),
                "A number carried in from an exterior list would blow a " +
                "face out at half a metre.");
            Assert.That(
                LastRouteCarCabinLight.GloveboxLampIntensity,
                Is.LessThan(0.2f),
                "The glovebox bulb throws nine centimetres; anything more " +
                "is a torch in a drawer.");
            Assert.That(
                LastRouteCarCabinLight.CabinLampIntensity,
                Is.LessThan(LastRouteCarHeadlights.StandingBeamIntensity),
                "The cabin lamp has grown past the car's own dipped beam.");
        }

        /// <summary>
        /// Both lamps burn from inside the thing that is drawn to be
        /// emitting them - the plafond's lens and the glovebox bulb.
        ///
        /// This is the guard against the two ways this silently rots: a
        /// constant here drifting away from the generator's coordinates, and
        /// the `100x` unit factor that has bitten this project's imported
        /// models before. It also enforces the canon, which is that light in
        /// this world has a visible cause.
        /// </summary>
        [Test]
        public void CabinLamps_OriginateInsideTheirDrawnLenses()
        {
            LastRouteCarAssetRegistry car = BuildCar(
                out GameObject parent,
                out Transform root);
            try
            {
                LastRouteCarCabinLight cabin = Occupy(root);
                AssertInsideDrawnPart(
                    car,
                    LastRouteCarCabinLight.CabinLampLensRole,
                    cabin.CabinLamp);
                AssertInsideDrawnPart(
                    car,
                    LastRouteCarCabinLight.GloveboxBulbRole,
                    cabin.GloveboxLamp);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        private static void AssertInsideDrawnPart(
            LastRouteCarAssetRegistry car,
            string role,
            Light lamp)
        {
            Renderer drawn = null;
            for (int index = 0; index < car.Bindings.Count; index++)
            {
                if (car.Bindings[index].Role == role)
                {
                    drawn = car.Bindings[index].Renderer;
                }
            }

            Assert.That(
                drawn,
                Is.Not.Null,
                $"Nothing is drawn for '{role}', so the prefab predates " +
                "the cabin fixtures. Rebuild it.");
            Assert.That(lamp, Is.Not.Null);
            Bounds bounds = drawn.bounds;
            bounds.Expand(0.06f);
            Assert.That(
                bounds.Contains(lamp.transform.position),
                Is.True,
                $"{lamp.name} burns from " +
                $"{lamp.transform.position} while the thing drawn to emit " +
                $"it stands at {drawn.bounds.center}. Light in this world " +
                "comes from something you can see.");
        }

        /// <summary>
        /// Shadowless and halo-less, and both for stated reasons. Shadows
        /// would not contain this lamp - the glass casts none - so they
        /// would cost an atlas slice and buy nothing. A halo belongs on a
        /// fixture seen through fog from across a yard, not on one seen from
        /// forty centimetres inside a closed car.
        /// </summary>
        [Test]
        public void CabinLamps_ShedNoShadowsAndCarryNoHalo()
        {
            BuildCar(out GameObject parent, out Transform root);
            try
            {
                LastRouteCarCabinLight cabin = Occupy(root);
                foreach (Light lamp in new[]
                         {
                             cabin.CabinLamp,
                             cabin.GloveboxLamp
                         })
                {
                    Assert.That(lamp, Is.Not.Null);
                    Assert.That(
                        lamp.shadows,
                        Is.EqualTo(LightShadows.None),
                        "The windscreen casts no shadow, so a shadowed " +
                        "cabin lamp escapes onto the bonnet anyway.");
                    Assert.That(
                        lamp.renderMode,
                        Is.EqualTo(LightRenderMode.ForcePixel));
                    Assert.That(
                        lamp.GetComponent<CityLightHalo>(),
                        Is.Null,
                        "A halo inside a closed cabin is a bloom on a lamp " +
                        "the player's face is already next to.");
                }
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        /// <summary>
        /// The trap that made a new material slot necessary at all.
        ///
        /// The instrument faces used to be authored on `Plate` - and so is
        /// the number plate hanging off the nose of the car. Lighting the
        /// dials on that slot would have set the plate glowing in the dark,
        /// on a wreck whose whole design is that nothing on it works. This
        /// is the only test in the repository that reads a binding's
        /// material slot, and it exists to pin exactly that.
        /// </summary>
        [Test]
        public void PanelFaces_AreLitAndTheNumberPlateIsNot()
        {
            LastRouteCarAssetRegistry car = BuildCar(
                out GameObject parent,
                out Transform root);
            try
            {
                Assert.That(root, Is.Not.Null);
                LastRouteCarMaterialSlot faces = SlotOf(car, "instrument_faces");
                LastRouteCarMaterialSlot plate = SlotOf(car, "number_plate");
                Assert.That(
                    faces,
                    Is.EqualTo(LastRouteCarMaterialSlot.CabinLamp),
                    "The gauge faces are back on a slot they share with the " +
                    "outside of the car.");
                Assert.That(
                    plate,
                    Is.EqualTo(LastRouteCarMaterialSlot.Plate),
                    "The number plate has moved onto a lit slot.");
                Assert.That(
                    faces,
                    Is.Not.EqualTo(plate),
                    "The dials and the number plate share a material again, " +
                    "so lighting one lights the other.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        private static LastRouteCarMaterialSlot SlotOf(
            LastRouteCarAssetRegistry car,
            string role)
        {
            for (int index = 0; index < car.Bindings.Count; index++)
            {
                if (car.Bindings[index].Role == role)
                {
                    return car.Bindings[index].MaterialSlot;
                }
            }

            Assert.Fail(
                $"Nothing is bound for '{role}'; the prefab is stale.");
            return default;
        }

        /// <summary>
        /// §20's half of the arrangement: the fixture itself is lit at every
        /// hour, on every car, whether or not anybody is in it. The realtime
        /// pool is what waits for an occupant.
        /// </summary>
        [Test]
        public void LitLens_BurnsOnAnEmptyCarAtEveryHour()
        {
            BuildCar(out GameObject parent, out Transform root);
            try
            {
                var cabin = root.GetComponent<LastRouteCarCabinLight>();
                Assert.That(cabin, Is.Not.Null);
                Assert.That(cabin.IsInitialized, Is.True);
                Assert.That(
                    cabin.ReadLensEmission().maxColorComponent,
                    Is.GreaterThan(0.05f),
                    "The plafond's lens is black on a parked car, so the " +
                    "one thing that says somebody is waiting in it is off.");
                Assert.That(
                    cabin.ReadPanelEmission().maxColorComponent,
                    Is.GreaterThan(0.05f),
                    "The instrument faces are dark, which is the half of " +
                    "the panel no lamp in the cabin can light well.");
                Assert.That(
                    cabin.ReadGloveboxEmission().maxColorComponent,
                    Is.LessThan(0.01f),
                    "The glovebox bulb shows through a shut dash face.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }
    }
}
