using System;
using System.Reflection;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class PlayerBuildAssetValidationTests
    {
        private static Type Validator => Type.GetType(
            "BarPromenade.Editor.PlayerBuildAssetValidation, BarPromenade.Editor", true);

        [Test]
        public void CurrentProject_PassesReadOnlyGate()
        {
            try
            {
                Validator.GetMethod("ValidateOrThrow").Invoke(null, null);
            }
            catch (TargetInvocationException failure)
            {
                Assert.Fail((failure.InnerException ?? failure).ToString());
            }
        }

        [TestCase("old", "new", true)]
        [TestCase("", "new", true)]
        [TestCase("same", "same", false)]
        [TestCase("", "", true)]
        public void DependencyGate_RejectsMissingOrStaleStamps(string actual, string expected, bool fails)
        {
            MethodInfo method = Validator.GetMethod("ValidateStamp");
            Assert.That(method, Is.Not.Null);
            if (fails)
            {
                TargetInvocationException failure = Assert.Throws<TargetInvocationException>(
                    () => method.Invoke(null, new object[] { "Assets/example.prefab", actual, expected }));
                Assert.That(failure.InnerException, Is.TypeOf<InvalidOperationException>());
                Assert.That(failure.InnerException.Message, Does.Contain("Assets/example.prefab"));
            }
            else
            {
                Assert.DoesNotThrow(() => method.Invoke(null,
                    new object[] { "Assets/example.prefab", actual, expected }));
            }
        }

        [Test]
        public void FailureReport_ContinuesAfterFirstFailureAndNamesEachRepair()
        {
            int visited = 0;
            Action[] checks =
            {
                () => { visited++; throw new InvalidOperationException("missing source"); },
                () => { visited++; },
                () => { visited++; throw new InvalidOperationException("stale prefab"); }
            };
            string[] labels = { "A / RepairA", "B / RepairB", "C / RepairC" };
            var failures = (string[])Validator.GetMethod("CollectFailures").Invoke(
                null, new object[] { labels, checks });
            Assert.That(visited, Is.EqualTo(3));
            Assert.That(failures.Length, Is.EqualTo(2));
            Assert.That(failures[0], Does.Contain("RepairA").And.Contain("missing source"));
            Assert.That(failures[1], Does.Contain("RepairC").And.Contain("stale prefab"));
        }

        [Test]
        public void Gate_IsInstalledForEveryPlayerBuildAndUsesExistingHeroSignature()
        {
            Assert.That(Array.Exists(Validator.GetInterfaces(),
                type => type.FullName == "UnityEditor.Build.IPreprocessBuildWithReport"), Is.True);
            Type setup = Type.GetType("BarPromenade.Editor.Player3DV2AssetSetup, BarPromenade.Editor", true);
            MethodInfo signature = setup.GetMethod("CalculateBuildSignature",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(signature, Is.Not.Null);
            Assert.That(signature.ReturnType, Is.EqualTo(typeof(string)));
            Assert.That(signature.GetParameters(), Is.Empty);
        }
    }
}
