using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The Ferryman and his car are drawn by two different generators that
    /// know nothing about each other, and they touch in three places: he
    /// sits on the bonnet, his boots rest on the bumper, and he later sits
    /// inside the cabin. Nothing in Blender links those.
    ///
    /// So both generators write their half of each contact into their own
    /// manifest, and this reads BOTH files and fails when they disagree.
    /// The failure mode it exists to catch is the quiet one: either
    /// generator can be moved four centimetres, stay green on its own, and
    /// leave a man floating beside a car or wearing its roof.
    /// </summary>
    public sealed class LastRouteFerrymanAssetTests
    {
        private const string CarManifestPath =
            "Assets/Vehicles/Models/LastRouteCar3D.json";
        private const string FerrymanManifestPath =
            "Assets/Pedestrians/Staged/Models/LastRouteFerryman3D.json";
        private const string AnimationManifestPath =
            "Assets/Pedestrians/Animations/CityPedestrianLocomotion.json";

        /// <summary>
        /// How far the two generators may disagree about a contact before
        /// it reads as a mistake. Three centimetres is about the point at
        /// which a seated man visibly stops touching what he is sitting on.
        /// </summary>
        private const float ContactTolerance = 0.03f;

        [Test]
        public void Bonnet_CarriesHimAtTheHeightHeWasAuthoredFor()
        {
            CarManifest car = LoadCar();
            AnimationClipManifest wait = RequireClip("FerrymanWait");

            Assert.That(
                wait.perched,
                Is.True,
                "His wait loop must be proved against a perch rather " +
                "than against a cabin.");
            Assert.That(
                wait.perch_seat_height_min_m,
                Is.EqualTo(car.perch_drop_m).Within(ContactTolerance),
                $"His seat sits {wait.perch_seat_height_min_m:0.####} m " +
                $"over his soles but the car's bonnet is " +
                $"{car.perch_drop_m:0.####} m over its bumper.");
            Assert.That(
                wait.perch_seat_height_max_m,
                Is.EqualTo(car.perch_drop_m).Within(ContactTolerance));
        }

        [Test]
        public void PerchBand_BracketsTheDrawnBonnet()
        {
            CarManifest car = LoadCar();
            FerrymanManifest ferryman = LoadFerryman();

            Assert.That(
                ferryman.perch_seat_height_m,
                Is.Not.Null.And.Length.EqualTo(2),
                "The Ferryman must declare a perch band.");
            Assert.That(
                car.perch_drop_m,
                Is.InRange(
                    ferryman.perch_seat_height_m[0] - ContactTolerance,
                    ferryman.perch_seat_height_m[1] + ContactTolerance),
                "The car's bonnet is outside the band his pose was " +
                "converged against.");
        }

        [Test]
        public void Cabin_HasRoomForHisCap()
        {
            CarManifest car = LoadCar();
            AnimationClipManifest drive = RequireClip("FerrymanDrive");

            Assert.That(
                drive.seated,
                Is.True,
                "His driving loop must be proved as a seated clip.");
            Assert.That(
                drive.perched,
                Is.False,
                "He drives in a cabin, not on a bonnet.");
            Assert.That(
                drive.seated_headroom_m,
                Is.LessThanOrEqualTo(car.seated_headroom_m),
                $"He measures {drive.seated_headroom_m:0.####} m from his " +
                $"seated pelvis to the top of his cap and the car offers " +
                $"{car.seated_headroom_m:0.####} m: he wears the roof.");
        }

        [Test]
        public void Cabin_HasRoomForHisLegs()
        {
            CarManifest car = LoadCar();
            AnimationClipManifest drive = RequireClip("FerrymanDrive");

            Assert.That(
                car.cabin_floor_drop_m,
                Is.GreaterThan(0f),
                "The car must publish how far its seat sits above its " +
                "floor pan.");
            Assert.That(
                drive.seated_floor_drop_limit_m,
                Is.EqualTo(car.cabin_floor_drop_m).Within(0.0005f),
                "His archetype was validated against a different floor " +
                "than the one the car draws.");
            Assert.That(
                drive.seated_drop_m,
                Is.LessThanOrEqualTo(car.cabin_floor_drop_m + 0.0005f),
                $"He hangs {drive.seated_drop_m:0.####} m of leg under a " +
                $"seat that is only {car.cabin_floor_drop_m:0.####} m " +
                "above the floor pan.");
        }

        [Test]
        public void BoardTransition_IsNotDeclaredALoop()
        {
            AnimationClipManifest board = RequireClip("FerrymanBoard");

            Assert.That(
                board.one_shot,
                Is.True,
                "Getting into a car happens once.");
            Assert.That(
                board.loop,
                Is.False,
                "A consumer reading this as a loop would repeat a man " +
                "jumping off a bonnet forever.");
        }

        [Test]
        public void Ferryman_DeclaresBothOfHisSeats()
        {
            FerrymanManifest ferryman = LoadFerryman();

            // He is the library's only design with two, and each of his
            // seated clips names the one that carries it. Losing either
            // band silently stops proving one of the two contacts.
            Assert.That(
                ferryman.perch_seat_height_m,
                Is.Not.Null.And.Length.EqualTo(2));
            Assert.That(
                ferryman.seated_clearance_m,
                Is.Not.Null.And.Length.EqualTo(2));
            CollectionAssert.Contains(
                ferryman.shared_clips,
                "FerrymanWait");
            CollectionAssert.Contains(
                ferryman.shared_clips,
                "FerrymanDrive");
            CollectionAssert.Contains(
                ferryman.shared_clips,
                "FerrymanBoard");
        }

        // -------------------------------------------------------- helpers

        private static AnimationClipManifest RequireClip(string name)
        {
            AnimationLibraryManifest library =
                LoadJson<AnimationLibraryManifest>(AnimationManifestPath);
            for (int index = 0; index < library.clips.Length; index++)
            {
                if (string.Equals(
                        library.clips[index].name,
                        name,
                        StringComparison.Ordinal))
                {
                    return library.clips[index];
                }
            }

            Assert.Fail($"The animation library has no clip '{name}'.");
            return null;
        }

        private static CarManifest LoadCar()
        {
            return LoadJson<CarManifest>(CarManifestPath);
        }

        private static FerrymanManifest LoadFerryman()
        {
            return LoadJson<FerrymanManifest>(FerrymanManifestPath);
        }

        private static T LoadJson<T>(string assetPath) where T : class
        {
            TextAsset asset =
                UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(
                    assetPath);
            Assert.That(
                asset,
                Is.Not.Null,
                $"Expected a manifest at '{assetPath}'.");

            T parsed = JsonUtility.FromJson<T>(asset.text);
            Assert.That(
                parsed,
                Is.Not.Null,
                $"'{assetPath}' is not readable as {typeof(T).Name}.");
            return parsed;
        }

        [Serializable]
        private sealed class CarManifest
        {
            public string design_id = string.Empty;
            public float perch_drop_m;
            public float seated_headroom_m;
            public float cabin_floor_drop_m;
        }

        [Serializable]
        private sealed class FerrymanManifest
        {
            public string design_id = string.Empty;
            public float[] perch_seat_height_m = Array.Empty<float>();
            public float[] seated_clearance_m = Array.Empty<float>();
            public string[] shared_clips = Array.Empty<string>();
        }

        [Serializable]
        private sealed class AnimationLibraryManifest
        {
            public AnimationClipManifest[] clips =
                Array.Empty<AnimationClipManifest>();
        }

        [Serializable]
        private sealed class AnimationClipManifest
        {
            public string name = string.Empty;
            public bool loop;
            public bool one_shot;
            public bool seated;
            public bool perched;
            public float perch_seat_height_min_m;
            public float perch_seat_height_max_m;
            public float seated_headroom_m;
            public float seated_drop_m;
            public float seated_floor_drop_limit_m;
        }
    }
}
