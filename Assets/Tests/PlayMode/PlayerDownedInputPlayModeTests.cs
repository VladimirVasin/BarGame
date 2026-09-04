using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The keys while he is down: with the physics on him a held key jerks
    /// the body that way and shortens the stun; once he is on all fours a
    /// held key keeps him there and crawls him along, hands alternating;
    /// released, he kneels and stands as before.
    /// </summary>
    public sealed class PlayerDownedInputPlayModeTests
    {
        private const float PinnedFrameSeconds = 1f / 60f;
        private const int TestCitySeed = 4244;

        private GameObject groundObject;
        private GameObject cameraObject;
        private GameObject playerObject;
        private GameObject uiObject;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.captureDeltaTime = PinnedFrameSeconds;
            ResetSession();
            GameSessionState.SetCitySeed(TestCitySeed);

            groundObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundObject.name = "Downed Input Ground";
            groundObject.transform.position = new Vector3(0f, -0.1f, 0f);
            groundObject.transform.localScale = new Vector3(14f, 0.2f, 14f);

            cameraObject = new GameObject("Downed Input Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (uiObject != null)
            {
                Object.Destroy(uiObject);
            }

            if (playerObject != null)
            {
                Object.Destroy(playerObject);
            }

            if (cameraObject != null)
            {
                Object.Destroy(cameraObject);
            }

            if (groundObject != null)
            {
                Object.Destroy(groundObject);
            }

            ResetSession();
            Time.captureDeltaTime = 0f;
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator DownedInput_TwitchesTheRagdollAndCrawlsOnAllFours()
        {
            PlayerRuntime hero = PlayerFactory.Create(
                null,
                Vector3.up * PlayerFactory.GroundedRootOffset,
                cameraObject.GetComponent<Camera>(),
                null,
                null);
            playerObject = hero.GameObject;
            Physics.SyncTransforms();
            var presentation = (Player3DCharacterPresentation)hero.Visual;
            IntoxicationStatusController status = CreateStatus(hero, 100);
            Player3DRagdollController ragdoll = hero.Ragdoll;
            status.Balance.ArmGrace(0f);
            for (int frame = 0; frame < 90; frame++)
            {
                yield return null;
            }

            Assert.That(presentation.IntoxicationAmount, Is.GreaterThan(0.9f));
            float deadline = Time.realtimeSinceStartup + 6f;
            while (!status.Balance.FallAllowedNow && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(status.Balance.FallAllowedNow, Is.True);
            status.Balance.InjectPerturbation(new Vector2(3f, 0f));
            deadline = Time.realtimeSinceStartup + 8f;
            while (status.BalanceStateName != "Down" && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(status.BalanceStateName, Is.EqualTo("Down"), "the shove must floor him");
            Assert.That(ragdoll.IsSimulating, Is.True);
            // The rise model is made on the first update he spends down.
            yield return null;
            Assert.That(status.Rise, Is.Not.Null);

            // A key while the physics has him: the chest jerks the key's
            // way (the camera's forward, +Z here) and the stun to come is
            // shorter for it.
            float stunBefore = status.Rise.StunSeconds;
            Vector3 chestBefore = ragdoll.ChestBody.transform.position;
            Vector3 chestVelocityBefore = ragdoll.ChestBody.linearVelocity;
            status.DebugDownedInput(new Vector2(0f, 1f));
            yield return null;
            yield return new WaitForFixedUpdate();
            float kicked = Vector3.Dot(ragdoll.ChestBody.linearVelocity - chestVelocityBefore, Vector3.forward);
            for (int frame = 0; frame < 20; frame++)
            {
                yield return null;
            }

            float pushed = Vector3.Dot(ragdoll.ChestBody.transform.position - chestBefore, Vector3.forward);
            Debug.Log($"Downed twitch: chest kicked {kicked:F2} m/s, moved {pushed:F3} m the key's way in a third of a second");
            Assert.That(kicked, Is.GreaterThan(0.3f), "the chest is kicked the key's way");
            Assert.That(pushed, Is.GreaterThan(0.01f), "and moves that way against the floor's friction");
            Assert.That(
                status.Rise.StunSeconds,
                Is.LessThan(stunBefore - 0.01f).Or.EqualTo(PlayerRiseRules.StunFloorSeconds),
                "a twitch shortens the stun");
            status.DebugDownedInput(Vector2.zero);

            // On all fours with the key held: he crawls, and the capsule
            // goes with him.
            deadline = Time.realtimeSinceStartup + 20f;
            while ((status.Rise == null || status.Rise.Stage < PlayerRiseStage.PushingUp) &&
                   status.IsFalling &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(status.IsFalling, Is.True);
            Assert.That(status.Rise.Stage, Is.EqualTo(PlayerRiseStage.PushingUp));
            status.DebugDownedInput(new Vector2(0f, 1f));
            deadline = Time.realtimeSinceStartup + 10f;
            while (status.Rise.Stage != PlayerRiseStage.Crawling &&
                   status.IsFalling &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(status.Rise.Stage, Is.EqualTo(PlayerRiseStage.Crawling), "a key held on all fours starts a crawl");
            Vector3 rootAtCrawlStart = playerObject.transform.position;
            Player3DAssetRegistry registry = presentation.Registry;
            Transform leftKnee = Bone(registry, Player3DAnatomicalPart.LeftShin);
            Transform rightKnee = Bone(registry, Player3DAnatomicalPart.RightShin);
            Transform leftHand = Bone(registry, Player3DAnatomicalPart.LeftHand);
            Transform rightHand = Bone(registry, Player3DAnatomicalPart.RightHand);
            float leftLift = 0f;
            float rightLift = 0f;
            float travelled = 0f;
            var leftKneeHeights = new List<float>();
            var rightKneeHeights = new List<float>();
            float lowestHand = float.PositiveInfinity;
            float highestHand = float.NegativeInfinity;
            // A planted hand holds its spot in the world while the body
            // crawls over it: watch one hand through its SECOND plant (the
            // hips are still settling down to the floor through the first).
            float plantedHandDrift = 0f;
            float rootDriftWhilePlanted = 0f;
            Vector3 plantedHand = Vector3.zero;
            Vector3 rootAtPlant = Vector3.zero;
            bool wasPlanted = true;
            int plants = 0;
            deadline = Time.realtimeSinceStartup + 10f;
            while ((travelled < 0.4f || plants < 3) && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                Assert.That(status.Rise.Stage, Is.EqualTo(PlayerRiseStage.Crawling), "the crawl lasts while the key is held");
                presentation.ReapplyLatePresentationPose();
                leftLift = Mathf.Max(leftLift, status.Rise.Output.LeftHandLift);
                rightLift = Mathf.Max(rightLift, status.Rise.Output.RightHandLift);
                leftKneeHeights.Add(leftKnee.position.y);
                rightKneeHeights.Add(rightKnee.position.y);
                lowestHand = Mathf.Min(lowestHand, Mathf.Min(leftHand.position.y, rightHand.position.y));
                highestHand = Mathf.Max(highestHand, Mathf.Max(leftHand.position.y, rightHand.position.y));
                bool planted = !status.Rise.Output.LeftHandCrawl.Swinging;
                if (planted && !wasPlanted)
                {
                    plants++;
                    plantedHand = leftHand.position;
                    rootAtPlant = playerObject.transform.position;
                }
                else if (planted && plants >= 2)
                {
                    plantedHandDrift = Mathf.Max(plantedHandDrift, Vector3.Distance(leftHand.position, plantedHand));
                    rootDriftWhilePlanted = Mathf.Max(rootDriftWhilePlanted, Vector3.Distance(playerObject.transform.position, rootAtPlant));
                }

                wasPlanted = planted;
                Vector3 offset = playerObject.transform.position - rootAtCrawlStart;
                offset.y = 0f;
                travelled = offset.magnitude;
            }

            leftKneeHeights.Sort();
            rightKneeHeights.Sort();
            float leftKneeLow = leftKneeHeights[0];
            float rightKneeLow = rightKneeHeights[0];
            float leftKneeHigh = leftKneeHeights[leftKneeHeights.Count - 1];
            float rightKneeHigh = rightKneeHeights[rightKneeHeights.Count - 1];
            Debug.Log(
                $"Crawl: travelled {travelled:F2} m; knees low/high L {leftKneeLow:F3}/{leftKneeHigh:F3} R {rightKneeLow:F3}/{rightKneeHigh:F3}; " +
                $"hands low/high {lowestHand:F3}/{highestHand:F3}; planted hand drifted {plantedHandDrift:F3} m while the root moved {rootDriftWhilePlanted:F3} m");

            Assert.That(travelled, Is.GreaterThanOrEqualTo(0.4f), "the crawl carries the capsule");
            Assert.That(leftLift, Is.GreaterThan(0.04f), "the left hand swings");
            Assert.That(rightLift, Is.GreaterThan(0.04f), "the right hand swings");
            // On the floor, not over it: each knee rests within a hand of
            // the floor, and each lifts to swing.
            Assert.That(leftKneeLow, Is.LessThan(0.12f), "the left knee rests on the floor");
            Assert.That(rightKneeLow, Is.LessThan(0.12f), "the right knee rests on the floor");
            Assert.That(leftKneeLow, Is.GreaterThan(-0.02f), "and never under it");
            Assert.That(rightKneeLow, Is.GreaterThan(-0.02f), "and never under it");
            Assert.That(leftKneeHigh - leftKneeLow, Is.GreaterThan(0.03f), "the left knee lifts to swing");
            Assert.That(rightKneeHigh - rightKneeLow, Is.GreaterThan(0.03f), "the right knee lifts to swing");
            Assert.That(lowestHand, Is.LessThan(0.12f), "the hands reach the floor");
            Assert.That(highestHand - lowestHand, Is.GreaterThan(0.05f), "the hands lift to swing");
            Assert.That(rootDriftWhilePlanted, Is.GreaterThan(0.05f), "the body moved over a planted hand");
            Assert.That(
                plantedHandDrift,
                Is.LessThan(0.06f),
                "a planted hand holds its spot in the world");
            Assert.That(
                Vector3.Angle(playerObject.transform.forward, Vector3.forward),
                Is.LessThan(60f),
                "he has turned toward the key");

            // Released: the kneel resumes and the capsule stands still.
            status.DebugDownedInput(Vector2.zero);
            deadline = Time.realtimeSinceStartup + 4f;
            while (status.Rise.Stage == PlayerRiseStage.Crawling && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(status.Rise.Stage, Is.EqualTo(PlayerRiseStage.Kneeling));
            Vector3 rootAtKneel = playerObject.transform.position;
            for (int frame = 0; frame < 30; frame++)
            {
                yield return null;
            }

            Vector3 drift = playerObject.transform.position - rootAtKneel;
            drift.y = 0f;
            Assert.That(drift.magnitude, Is.LessThan(0.02f), "no key, no crawl");

            deadline = Time.realtimeSinceStartup + 12f;
            while (status.IsFalling && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(status.IsFalling, Is.False, "he gets up after the crawl");
        }

        private static Transform Bone(Player3DAssetRegistry registry, Player3DAnatomicalPart part)
        {
            Assert.That(registry.TryGetPart(part, out var binding), Is.True, $"{part} is not registered");
            Assert.That(binding.Bone, Is.Not.Null, $"{part} has no bone");
            return binding.Bone;
        }

        private IntoxicationStatusController CreateStatus(
            PlayerRuntime hero,
            int level)
        {
            GameSessionState.UpdateDrinkingProgress(level, DrinkId.Vodka, 5);
            uiObject = new GameObject("Downed Input UI");
            IntoxicationHudView hud = uiObject.AddComponent<IntoxicationHudView>();
            var followCameraObject = new GameObject("Downed Input Follow");
            followCameraObject.transform.SetParent(uiObject.transform, false);
            Camera followCamera = followCameraObject.AddComponent<Camera>();
            followCamera.enabled = false;
            var follow = followCameraObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(followCamera, hero.GameObject.transform, false);
            follow.enabled = false;

            IntoxicationStatusController status =
                uiObject.AddComponent<IntoxicationStatusController>();
            status.Initialize(hero, follow, hud);
            return status;
        }

        private static void ResetSession()
        {
            GameSessionState.SetCitySeed(GameSessionState.DefaultCitySeed);
            GameSessionState.EnterBar(null);
            GameSessionState.CompleteCityReturn();
            GameSessionState.ResetDrinkingState();
        }
    }
}
