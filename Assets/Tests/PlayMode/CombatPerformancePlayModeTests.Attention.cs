using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatPerformancePlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_MutualAttentionUsesLiveHeadsAndReleasesItsOwner()
        {
            PlacePair(2f);
            Vector3 origin = root.Hero.transform.position;
            root.Opponent.ResetActor(origin + new Vector3(1f, 0f, 2f), Vector3.back);
            Physics.SyncTransforms();
            var presentation = (Player3DCharacterPresentation)root.Player.Visual;
            Transform heroHead = presentation.Registry.Anchors.Head;
            Transform npcHead = root.Opponent.GetComponentInChildren<VillageResidentPresentation>().Head;
            Quaternion heroBase = heroHead.localRotation, npcBase = npcHead.localRotation;
            for (int frame = 0; frame < 20; frame++) { root.Tick(1f / 60f); yield return null; }
            Assert.That(root.Hero.CombatAttentionWeight, Is.GreaterThan(.95f));
            Assert.That(root.Opponent.CombatAttentionWeight, Is.GreaterThan(.95f));
            AssertCombatAttentionTracksLiveHeads(heroHead, npcHead);
            Assert.That(Quaternion.Angle(heroBase, heroHead.localRotation), Is.GreaterThan(3f),
                "The hero's existing attention layer must turn his real head despite the owned combat stance.");
            Assert.That(Quaternion.Angle(npcBase, npcHead.localRotation), Is.GreaterThan(3f),
                "The shared NPC layer must turn the original opponent rig.");

            Camera camera = root.CameraFollow.Camera;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            try
            {
                Vector3 faces = (heroHead.position + npcHead.position) * .5f;
                camera.transform.position = faces + new Vector3(-3f, .18f, -.3f);
                camera.transform.rotation = Quaternion.LookRotation(faces - camera.transform.position);
                LogAssert.Expect(LogType.Log, "Area capture wrote " + Path.Combine(Directory.GetCurrentDirectory(),
                    "Captures", SceneIds.CombatTest, "combat-mutual-gaze.png"));
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest, "combat-mutual-gaze");
            }
            finally { camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation); }

            Quaternion heroRight = heroHead.localRotation, npcRight = npcHead.localRotation;
            root.Opponent.ResetActor(origin + new Vector3(-1f, 0f, 2f), Vector3.back);
            Physics.SyncTransforms();
            for (int frame = 0; frame < 20; frame++) { root.Tick(1f / 60f); yield return null; }
            AssertCombatAttentionTracksLiveHeads(heroHead, npcHead);
            Assert.That(Quaternion.Angle(heroRight, heroHead.localRotation), Is.GreaterThan(6f),
                "The hero must follow the opponent's changed head position instead of holding a captured point.");
            Assert.That(Quaternion.Angle(npcRight, npcHead.localRotation), Is.GreaterThan(6f));

            Quaternion heroPaused = heroHead.localRotation, npcPaused = npcHead.localRotation;
            float heroWeight = root.Hero.CombatAttentionWeight, npcWeight = root.Opponent.CombatAttentionWeight;
            Assert.That(root.PauseMenu.Open(), Is.True);
            for (int frame = 0; frame < 4; frame++) { root.Tick(.2f); yield return null; }
            Assert.That(presentation.OwnsClip(root.Hero), Is.True, "Pause retains the combat pose owner.");
            Assert.That(Quaternion.Angle(heroPaused, heroHead.localRotation), Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(npcPaused, npcHead.localRotation), Is.LessThan(.001f));
            Assert.That(root.Hero.CombatAttentionWeight, Is.EqualTo(heroWeight).Within(.0001f));
            Assert.That(root.Opponent.CombatAttentionWeight, Is.EqualTo(npcWeight).Within(.0001f));
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            for (int frame = 0; frame < 60 && !GameInput.CanRead(GameInputContext.Gameplay); frame++) yield return null;
            Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.True, "Pause did not release combat attention.");

            // Freeze the same complete pose the root supplies after impact,
            // rather than a graph base observed before this frame's late pass.
            root.Hero.Present(); root.Opponent.Present();
            root.Hero.SetPresentationFrozen(true);
            root.Opponent.SetPresentationFrozen(true);
            try
            {
                heroPaused = heroHead.localRotation; npcPaused = npcHead.localRotation;
                for (int frame = 0; frame < 4; frame++) yield return null;
                Assert.That(Quaternion.Angle(heroPaused, heroHead.localRotation), Is.LessThan(.001f),
                    "Local hit-stop must also freeze the hero's attention smoothing.");
                Assert.That(Quaternion.Angle(npcPaused, npcHead.localRotation), Is.LessThan(.001f));
            }
            finally { root.Hero.SetPresentationFrozen(false); root.Opponent.SetPresentationFrozen(false); }

            root.Opponent.State.ReceiveHit(root.Opponent.State.Settings.MaxHealth, 0f, false);
            root.Hero.Present(); root.Opponent.Present();
            Assert.That(root.Hero.CombatAttentionFocus, Is.Null, "A defeated target releases the gaze.");
            Assert.That(root.Opponent.CombatAttentionFocus, Is.Null);
            Assert.That(root.Opponent.CombatAttentionWeight, Is.Zero);
            root.ResetRound();
            Assert.That(root.Hero.CombatAttentionWeight, Is.Zero, "Reset drops the old smoothing and target.");
            Assert.That(root.Opponent.CombatAttentionWeight, Is.Zero);
            for (int frame = 0; frame < 20; frame++) { root.Tick(1f / 60f); yield return null; }
            Assert.That(root.Hero.CombatAttentionFocus, Is.Not.Null);
            Assert.That(root.Opponent.CombatAttentionFocus, Is.Not.Null);

            object otherOwner = new object();
            presentation.ReleaseOwnedClip(root.Hero);
            Assert.That(presentation.TryAcquireClip(otherOwner, "CombatHit"), Is.True);
            root.Hero.Present();
            yield return null;
            Assert.That(presentation.OwnsClip(otherOwner), Is.True);
            Assert.That(root.Hero.CombatAttentionFocus, Is.Null);
            Assert.That(presentation.AttentionWeight, Is.Zero,
                "Combat cannot keep head ownership inside another owner's modal clip.");
            root.Hero.enabled = false;
            root.Opponent.enabled = false;
            Assert.That(root.Opponent.CombatAttentionWeight, Is.Zero);
            Assert.That(presentation.OwnsClip(otherOwner), Is.True);
            presentation.ReleaseOwnedClip(otherOwner);
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
            Assert.That(Object.FindAnyObjectByType<CombatActor>(), Is.Null);
        }

        private void AssertCombatAttentionTracksLiveHeads(Transform heroHead, Transform npcHead)
        {
            // Each actor selects a live head before its own pose is sampled.
            // The other actor's later neck turn may move that point again, so
            // compare the selected focus to the pose that actually supplied it.
            Vector3 liveNpcHead = npcHead.position;
            root.Hero.Present();
            Assert.That(root.Hero.CombatAttentionFocus, Is.Not.Null);
            Assert.That(Vector3.Distance(root.Hero.CombatAttentionFocus.Value, liveNpcHead), Is.LessThan(.00001f),
                "The hero must select the opponent's current head at presentation time.");
            Vector3 liveHeroHead = heroHead.position;
            root.Opponent.Present();
            Assert.That(root.Opponent.CombatAttentionFocus, Is.Not.Null);
            Assert.That(Vector3.Distance(root.Opponent.CombatAttentionFocus.Value, liveHeroHead), Is.LessThan(.00001f),
                "The opponent must select the hero's current head at presentation time.");
        }
    }
}
