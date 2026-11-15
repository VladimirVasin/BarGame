using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class LastRouteFerrymanAssetTests
    {
        private const string CarPath = "Assets/Vehicles/Models/LastRouteCar3D.json";
        private const string DriverPath = "Assets/Resources/City/LastRouteFerrymanActions";
        private const string PassengerPath = "Assets/Resources/Player/LastRoutePassengerActions";

        [Test]
        public void CarActionsFitTheAuthoredSedan()
        {
            Car car = Read<Car>(CarPath);
            Bank driver = Read<Bank>(DriverPath + ".json");
            Bank passenger = Read<Bank>(PassengerPath + ".json");
            Assert.That(car.perch_seat_z - car.perch_soles_z, Is.EqualTo(car.perch_drop_m).Within(.001f));
            Assert.That(driver.contacts.driver_crown_above_pelvis_m + .045f,
                Is.LessThanOrEqualTo(car.seated_headroom_m));
            Assert.That(passenger.contacts.passenger_crown_above_pelvis_m + .055f,
                Is.LessThanOrEqualTo(car.seated_headroom_m));
            Assert.That(driver.contacts.maximum_sole_fit_error_m, Is.LessThan(.006f));
            Assert.That(passenger.contacts.maximum_sole_fit_error_m, Is.LessThan(.006f));
            Assert.That(driver.contacts.perch_hip_rise_m, Is.InRange(.055f, .115f));
            Assert.That(driver.contacts.minimum_perch_shin_clearance_m, Is.GreaterThanOrEqualTo(.013f));
        }

        [TestCase(DriverPath, 5)]
        [TestCase(PassengerPath, 3)]
        public void BanksImportOnTheSharedGenericRig(string path, int clipCount)
        {
            Bank bank = Read<Bank>(path + ".json");
            Assert.That(bank.clips, Has.Length.EqualTo(clipCount));
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path + ".fbx");
            Assert.That(model.GetComponentsInChildren<Renderer>(true), Is.Empty);
            var clips = AssetDatabase.LoadAllAssetsAtPath(path + ".fbx").OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            Assert.That(clips, Has.Length.EqualTo(clipCount));
            foreach (Take take in bank.clips)
            {
                AnimationClip clip = clips.Single(value => value.name == take.name);
                Assert.That(clip.isLooping, Is.EqualTo(take.loop));
                Assert.That(clip.length, Is.EqualTo(take.seconds).Within(.005f));
                Assert.That(clip.events, Is.Empty);
            }
        }

        private static T Read<T>(string path)
        {
            TextAsset data = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Assert.That(data, Is.Not.Null, path);
            return JsonUtility.FromJson<T>(data.text);
        }

        [Serializable] private sealed class Car
        {
            public float perch_seat_z, perch_soles_z, perch_drop_m, seated_headroom_m;
        }

        [Serializable] private sealed class Bank { public Take[] clips; public Contacts contacts; }
        [Serializable] private sealed class Take { public string name; public float seconds; public bool loop; }
        [Serializable] private sealed class Contacts
        {
            public float driver_crown_above_pelvis_m, passenger_crown_above_pelvis_m;
            public float maximum_sole_fit_error_m, perch_hip_rise_m;
            public float minimum_perch_shin_clearance_m;
        }
    }
}
