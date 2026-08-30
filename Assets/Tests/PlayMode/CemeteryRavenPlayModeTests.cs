using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// Scene-level contracts of the cemetery raven pair, end states
    /// only: every flight timing lives in EditMode on the pure
    /// models, because batchmode frame pacing makes trajectory
    /// assertions lies. Run ONE fixture per invocation — these load
    /// the whole City, and the project has already hit the
    /// test-runner's instant-step limit from stacking scene-loading
    /// fixtures.
    /// </summary>
    public sealed class CemeteryRavenPlayModeTests
    {
        private const float TimeoutSeconds = 30f;

        /// <summary>Generous: an arrival covers 46 m at glide speed
        /// after its stagger, and batchmode paces itself.</summary>
        private const float FlightTimeoutSeconds = 90f;

        [UnityTest]
        public IEnumerator
            RavensArriveAfterTheFirstSealAndPerchOnTheMoundAndTheGround()
        {
            IgnoreUnlessRavenPrefabBuilt();
            GameSessionState.BeginNewGame();
            (CityCemeteryPlan cemetery,
             CemeteryGravediggingPlan grave) = CreatePlans();
            string plotId = grave.Plot.StableId;
            Vector3 crown = CityCemeterySealedGraveWorldBuilder
                .GetMoundCrownPoint(grave);

            // Filled BEFORE the load: the seal itself must be the
            // live in-scene transition the director observes.
            Assert.That(
                GameSessionState.TryAdvanceGraveWork(
                    plotId,
                    CemeteryGraveWorkStage.Filled),
                Is.True);

            CityGameRoot city = null;
            yield return LoadSceneAndWaitForRoot<CityGameRoot>(
                SceneIds.City,
                root => city = root);
            yield return null;

            Assert.That(city.IsInitialized, Is.True);
            CityCemeteryRavenController ravens = city.CemeteryRavens;
            Assert.That(ravens, Is.Not.Null);
            Assert.That(
                ravens.IsArmed,
                Is.False,
                "No grave is sealed yet, so nothing may be armed.");

            // The hero steps well clear of the future perches before
            // the seal: birds do not land beside a standing man.
            city.Player.Motor.Teleport(
                crown + new Vector3(45f, 0.5f, 0f));
            yield return null;

            // The seal, written in-scene: the null-to-id transition
            // this build alone can witness.
            Assert.That(
                GameSessionState.TryAdvanceGraveWork(
                    plotId,
                    CemeteryGraveWorkStage.Sealed),
                Is.True);

            // NOTE, deliberate: the world stays in its Filled
            // dressing — mound, work lamp and spade, no monument —
            // under the Sealed ledger, because nothing rebuilds a
            // worksite mid-scene. The asserts below are POSITIONAL
            // only for exactly that reason: the perch math is pure
            // over the plan, not over the currently staged props.
            float deadline =
                Time.realtimeSinceStartup + FlightTimeoutSeconds;
            while (ravens.Phase != CemeteryRavenPhase.PerchedIdle &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                ravens.Phase,
                Is.EqualTo(CemeteryRavenPhase.PerchedIdle),
                "The arrival never landed.");
            Assert.That(ravens.RavenA, Is.Not.Null);
            Assert.That(ravens.RavenB, Is.Not.Null);
            // The controller derives the crown from the same pure
            // plan this test computed pre-load; a drift here means
            // the scene resolved a different grave.
            Assert.That(
                Vector3.Distance(ravens.MoundPerch.Position, crown),
                Is.LessThanOrEqualTo(0.001f));
            Assert.That(
                Vector3.Distance(
                    ravens.RavenA.transform.position,
                    crown),
                Is.LessThanOrEqualTo(0.05f),
                "Raven A perches on the mound crown.");
            Assert.That(ravens.GroundPerch.IsPresent, Is.True);
            Assert.That(
                ravens.GroundPerch.Position.y,
                Is.EqualTo(cemetery.GroundTopY).Within(0.001f));
            Assert.That(
                Vector3.Distance(
                    ravens.RavenB.transform.position,
                    ravens.GroundPerch.Position),
                Is.LessThanOrEqualTo(0.05f),
                "Raven B perches on the selected clear ground.");

            // The canon budget: two AmbienceDetails voices with the
            // village dog's audible radius, and no light anywhere.
            Assert.That(
                ravens.GetComponentsInChildren<Light>(true),
                Is.Empty);
            AudioSource[] sources =
                ravens.GetComponentsInChildren<AudioSource>(true);
            Assert.That(sources, Has.Length.EqualTo(2));
            Assert.That(GameAudioMixer.IsAvailable, Is.True);
            for (int index = 0; index < sources.Length; index++)
            {
                Assert.That(
                    sources[index].maxDistance,
                    Is.EqualTo(
                        CemeteryRavenVoice.AudibleRadiusMeters));
                Assert.That(
                    sources[index].outputAudioMixerGroup,
                    Is.SameAs(GameAudioMixer.AmbienceDetailsGroup));
            }
        }

        [UnityTest]
        public IEnumerator
            RavensFlushWhenApproachedAndReturnWhenTheHeroRetreats()
        {
            IgnoreUnlessRavenPrefabBuilt();
            GameSessionState.BeginNewGame();
            (CityCemeteryPlan _,
             CemeteryGravediggingPlan grave) = CreatePlans();
            Vector3 crown = CityCemeterySealedGraveWorldBuilder
                .GetMoundCrownPoint(grave);

            // Sealed BEFORE the load: the flag stands at the very
            // first poll and the pair spawns already sitting, with no
            // arrival to wait out.
            Assert.That(
                GameSessionState.TryAdvanceGraveWork(
                    grave.Plot.StableId,
                    CemeteryGraveWorkStage.Sealed),
                Is.True);

            CityGameRoot city = null;
            yield return LoadSceneAndWaitForRoot<CityGameRoot>(
                SceneIds.City,
                root => city = root);
            yield return null;

            CityCemeteryRavenController ravens = city.CemeteryRavens;
            Assert.That(ravens, Is.Not.Null);
            city.Player.Motor.Teleport(
                crown + new Vector3(45f, 0.5f, 0f));
            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (ravens.Phase != CemeteryRavenPhase.PerchedIdle &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                ravens.Phase,
                Is.EqualTo(CemeteryRavenPhase.PerchedIdle),
                "A pre-sealed ledger spawns the pair perched.");
            Assert.That(ravens.RavenA.IsPerched, Is.True);
            Assert.That(ravens.RavenB.IsPerched, Is.True);
            AudioSource[] sources =
                ravens.GetComponentsInChildren<AudioSource>(true);
            Assert.That(sources, Has.Length.EqualTo(2));

            // Arm's length of the mound bird: BOTH flush. The startle
            // cries must actually be playing somewhere in the window —
            // the automatic test mute silences the mixer, never the
            // sources' own playback.
            city.Player.Motor.Teleport(
                crown + new Vector3(2.8f, 0.5f, 0f));
            bool cawObserved = false;
            deadline =
                Time.realtimeSinceStartup + FlightTimeoutSeconds;
            while (ravens.Phase != CemeteryRavenPhase.Away &&
                   Time.realtimeSinceStartup < deadline)
            {
                for (int index = 0; index < sources.Length; index++)
                {
                    if (sources[index].isPlaying)
                    {
                        cawObserved = true;
                    }
                }

                yield return null;
            }

            Assert.That(
                ravens.Phase,
                Is.EqualTo(CemeteryRavenPhase.Away),
                "The flush never carried both birds past the fog.");
            Assert.That(
                cawObserved,
                Is.True,
                "No takeoff caw played during the startle window.");
            Assert.That(ravens.RavenA.IsPerched, Is.False);
            Assert.That(ravens.RavenB.IsPerched, Is.False);
            Assert.That(
                ravens.RavenA.Anchors.Renderers[0].enabled,
                Is.False,
                "A raven past the fog draws nothing.");
            Assert.That(
                ravens.RavenB.Anchors.Renderers[0].enabled,
                Is.False);

            // Retreat past the 70%-of-visibility gate: both return to
            // their own spots — the same points, not new ones.
            city.Player.Motor.Teleport(
                crown + new Vector3(45f, 0.5f, 0f));
            deadline =
                Time.realtimeSinceStartup + FlightTimeoutSeconds;
            while (ravens.Phase != CemeteryRavenPhase.PerchedIdle &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                ravens.Phase,
                Is.EqualTo(CemeteryRavenPhase.PerchedIdle),
                "The pair never came back after the retreat.");
            Assert.That(ravens.RavenA.IsPerched, Is.True);
            Assert.That(ravens.RavenB.IsPerched, Is.True);
            Assert.That(
                ravens.RavenA.Anchors.Renderers[0].enabled,
                Is.True);
            Assert.That(
                Vector3.Distance(
                    ravens.RavenA.transform.position,
                    ravens.MoundPerch.Position),
                Is.LessThanOrEqualTo(0.05f));
            Assert.That(
                Vector3.Distance(
                    ravens.RavenB.transform.position,
                    ravens.GroundPerch.Position),
                Is.LessThanOrEqualTo(0.05f));
        }

        /// <summary>
        /// The scene guard the asset suite uses too: until the raven
        /// prefab is built by the editor pipeline these fixtures have
        /// nothing to observe, and an Ignore says so honestly where a
        /// failure would cry wolf.
        /// </summary>
        private static void IgnoreUnlessRavenPrefabBuilt()
        {
            CemeteryRavenProvider provider =
                CemeteryRavenProvider.Load();
            if (provider == null || provider.RavenPrefab == null)
            {
                Assert.Ignore(
                    "The cemetery raven prefab is not built yet.");
            }
        }

        /// <summary>
        /// The same pure plans the City build derives, computed ahead
        /// of the load — the mourner tests' idiom, so the plot id in
        /// the ledger names ground the loaded city actually has.
        /// </summary>
        private static (CityCemeteryPlan, CemeteryGravediggingPlan)
            CreatePlans()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.CitySeed);
            CityCemeteryPlan cemetery =
                CityCemeteryPlanner.Create(layout);
            Assert.That(cemetery, Is.Not.Null,
                "The default city must carry a dressable cemetery.");
            CemeteryWatchmanPlan watchman =
                CemeteryWatchmanPlan.Create(cemetery);
            CemeteryGravediggingPlan grave =
                CemeteryGravediggingPlan.Create(cemetery, watchman);
            Assert.That(grave.IsPresent, Is.True);
            return (cemetery, grave);
        }

        private static IEnumerator LoadSceneAndWaitForRoot<T>(
            string sceneName,
            Action<T> capture)
            where T : Component
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (!operation.isDone &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(operation.isDone, Is.True);
            deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                T root = UnityEngine.Object.FindAnyObjectByType<T>();
                if (root != null)
                {
                    capture(root);
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(
                $"Scene '{sceneName}' never built its root.");
        }
    }
}
