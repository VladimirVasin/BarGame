using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private static void AssertForemanMouthPlacement(Renderer face, Transform biteAnchor)
        {
            // All authored mouth frames share pixel (32,50). Check the actual deformed
            // imported surface there: the old resident UV/patch put the mouth above the hollow.
            Vector2 mouthUV = new Vector2(.5f, 1f - 50f / 64f);
            var mesh = new Mesh();
            try
            {
                Assert.That(face, Is.InstanceOf<SkinnedMeshRenderer>());
                ((SkinnedMeshRenderer)face).BakeMesh(mesh, true);
                Vector2[] uv = mesh.uv;
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                bool found = false;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    Vector2 ab = uv[b] - uv[a], ac = uv[c] - uv[a], target = mouthUV - uv[a];
                    float determinant = ab.x * ac.y - ab.y * ac.x;
                    if (Mathf.Abs(determinant) < .000001f) continue;
                    float vb = (target.x * ac.y - target.y * ac.x) / determinant;
                    float vc = (ab.x * target.y - ab.y * target.x) / determinant;
                    if (vb < -.00001f || vc < -.00001f || vb + vc > 1.00001f) continue;
                    Vector3 point = face.localToWorldMatrix.MultiplyPoint3x4(
                        vertices[a] * (1f - vb - vc) + vertices[b] * vb + vertices[c] * vc);
                    Assert.That(Vector3.Distance(point, biteAnchor.position), Is.LessThan(.01f),
                        "The drawn mouth must occupy the lip/contact hollow, not the nose or upper face.");
                    found = true;
                    break;
                }
                Assert.That(found, Is.True, "The continuous face surface must extend through the mouth UV.");
            }
            finally { Object.Destroy(mesh); }
        }

        // Called by the existing foreman dialogue capture: no independent scene or test run.
        internal static void ValidateSpeechFaceContract()
        {
            ValidateSpeechFaceSampleLifetime();
            ValidateForemanChinDynamicsContract();
            string[][] localizedCues =
            {
                new[] { "ма", "ла", "ха", "уа", "еа", "фа" },
                new[] { "ma", "la", "ha", "wa", "ea", "fa" }
            };
            foreach (SpeechFaceProfile profile in Enum.GetValues(typeof(SpeechFaceProfile)))
            {
                foreach (string[] cues in localizedCues)
                    for (int mouth = 0; mouth < cues.Length; mouth++)
                        Assert.That(SpeechFaceAnimation.Resolve(Typing(cues[mouth], 1), profile, 1d).Mouth,
                            Is.EqualTo((SpeechMouthPose)mouth), "RU/EN articulation: " + cues[mouth]);

                const string punctuation = "Х ,—.?!;:\n… следом";
                for (int revealed = 2; revealed <= punctuation.IndexOf('с'); revealed++)
                    Assert.That(SpeechFaceAnimation.Resolve(Typing(punctuation, revealed), profile, 1d).Mouth,
                        Is.EqualTo(SpeechMouthPose.Closed), "Whitespace and punctuation close the mouth immediately.");
                var tail = new SpeechFaceSample(1, "Хочу", 4, true, .2d);
                Assert.That(tail.HasLine && !tail.IsTyping, Is.True);
                Assert.That(SpeechFaceAnimation.Resolve(tail, profile, 1d).Mouth, Is.EqualTo(SpeechMouthPose.Closed));
                Assert.That(SpeechFaceAnimation.Resolve(default, profile, 1d).Mouth, Is.EqualTo(SpeechMouthPose.Closed));

                var speaking = Typing("Хочу послушать.", 1);
                SpeechFacePose held = SpeechFaceAnimation.Resolve(speaking, profile, 1.4d);
                SpeechFaceAnimation.Resolve(Typing("Другой человек говорит.", 9), profile, 30d);
                Assert.That(SpeechFaceAnimation.Resolve(speaking, profile, 1.4d).AtlasCell, Is.EqualTo(held.AtlasCell),
                    "The same paused snapshot resolves identically, without shared state or caught-up frames.");
                bool sawBlink = false, sawReopenedEyes = false;
                for (int step = 0; step <= 100; step++)
                {
                    double clock = step * .05d;
                    SpeechFacePose pose = SpeechFaceAnimation.Resolve(speaking, profile, clock);
                    sawBlink |= pose.Expression == SpeechFaceExpression.Blink;
                    sawReopenedEyes |= pose.Expression == SpeechFaceExpression.Emphasis;
                    Assert.That(pose.Mouth, Is.EqualTo(held.Mouth), "Blinking composes with the held mouth shape.");
                    Assert.That(SpeechFaceAnimation.ResolveListening(profile, clock).Mouth,
                        Is.EqualTo(SpeechMouthPose.Closed), "Silent choices and listeners never mouth words.");
                }
                Assert.That(sawBlink && sawReopenedEyes, Is.True, "The eye layer blinks and reopens independently.");
            }

            SpeechFacePose first = SpeechFaceAnimation.Resolve(Typing("Хочу", 1), SpeechFaceProfile.Hero, 1d);
            SpeechFacePose third = SpeechFaceAnimation.Resolve(Typing("Хочу", 3), SpeechFaceProfile.Hero, 1d);
            Assert.That(first.Mouth, Is.Not.EqualTo(SpeechMouthPose.Closed));
            Assert.That(third.Mouth, Is.Not.EqualTo(SpeechMouthPose.Closed));
            Assert.That(third.Mouth, Is.Not.EqualTo(first.Mouth), "The existing four-letter reply has visible articulation before its reading tail.");
            var longLine = new SpeechFaceSample(2, "Работа сама себя не сделает.", 15, true, .7d);
            Assert.That(SpeechFaceAnimation.Resolve(longLine, SpeechFaceProfile.Hero, 1d).Expression,
                Is.Not.EqualTo(SpeechFaceAnimation.Resolve(longLine, SpeechFaceProfile.Foreman, 1d).Expression),
                "The weary hero rests between accents while the foreman holds his emphasis.");

            foreach (SpeechFaceExpression expression in Enum.GetValues(typeof(SpeechFaceExpression)))
                foreach (SpeechMouthPose mouth in Enum.GetValues(typeof(SpeechMouthPose)))
                {
                    int cell = new SpeechFacePose(mouth, expression).AtlasCell;
                    Assert.That(cell, Is.InRange(0, 29), "Every composed face fits the foreman's four atlas rows.");
                    Assert.That(cell + SpeechFaceAnimation.DirtyCellOffset, Is.InRange(32, 63),
                        "The hero's dirty variant remains inside its eight-row atlas.");
                }

            SpeechFaceSample Typing(string text, int count) =>
                new SpeechFaceSample(1, text, count, true, count / (double)SpeechDelivery.CharactersPerSecond);
        }

        internal static void ValidateForemanChinDynamicsContract()
        {
            var chin = new ForemanChinDynamics();
            chin.Advance(0d, SpeechMouthPose.Closed, false);
            ForemanChinDisplacement driven = chin.Advance(.05d, SpeechMouthPose.Open, true);
            Assert.That(driven.Y, Is.GreaterThan(.01d), "An opening mouth physically pulls the lower flesh.");
            Assert.That(chin.IsMoving, Is.True);
            ForemanChinDisplacement paused = chin.Advance(.05d, SpeechMouthPose.Closed, false, 90d, 90d);
            Assert.That(paused.X, Is.EqualTo(driven.X));
            Assert.That(paused.Y, Is.EqualTo(driven.Y));
            Assert.That(paused.Z, Is.EqualTo(driven.Z), "A duplicate paused clock neither advances nor queues changed inputs.");
            bool rebounded = false;
            for (int frame = 2; frame <= 80; frame++)
            {
                ForemanChinDisplacement settling = chin.Advance(frame * .05d, SpeechMouthPose.Closed, false);
                rebounded |= settling.Y < -.001d;
            }
            Assert.That(rebounded, Is.True, "Released flesh crosses rest from its own inertia; this is not a mouth-state lerp.");
            Assert.That(chin.IsMoving, Is.False, "The spring loses energy and settles after the speaking owner leaves.");
            ForemanChinDisplacement idle = chin.Advance(4.05d, SpeechMouthPose.Open, false, 30d, 40d);
            Assert.That(idle.X == 0d && idle.Y == 0d && idle.Z == 0d, Is.True,
                "Chewing and idle head changes cannot restart the speech spring.");

            chin.Reset();
            chin.Advance(0d, SpeechMouthPose.Closed, true);
            ForemanChinDisplacement headImpulse = chin.Advance(.05d, SpeechMouthPose.Closed, true, 5d, 7d);
            Assert.That(Math.Abs(headImpulse.X) + Math.Abs(headImpulse.Z), Is.GreaterThan(.01d),
                "A real head acceleration supplies inertia independently of the mouth.");
            var repeat = new ForemanChinDynamics();
            repeat.Advance(0d, SpeechMouthPose.Closed, true);
            repeat.Advance(.05d, SpeechMouthPose.Closed, true, 5d, 7d);
            for (int frame = 2; frame <= 80; frame++)
            {
                double time = frame * .025d + .05d;
                var mouth = (SpeechMouthPose)(frame % SpeechFaceAnimation.MouthCount);
                double pitch = Math.Sin(time * 13d) * 30d, yaw = Math.Cos(time * 17d) * 30d;
                ForemanChinDisplacement actual = chin.Advance(time, mouth, true, pitch, yaw);
                ForemanChinDisplacement identical = repeat.Advance(time, mouth, true, pitch, yaw);
                Assert.That(actual.X, Is.InRange(-1d, 1d));
                Assert.That(actual.Y, Is.InRange(-1d, 1d));
                Assert.That(actual.Z, Is.InRange(-1d, 1d), "Authored blendshape travel is bounded even under strong impulses.");
                Assert.That(actual.X, Is.EqualTo(identical.X));
                Assert.That(actual.Y, Is.EqualTo(identical.Y));
                Assert.That(actual.Z, Is.EqualTo(identical.Z));
            }
            ForemanChinDisplacement seek = chin.Advance(20d, SpeechMouthPose.Open, true, 170d, 170d);
            Assert.That(seek.X == 0d && seek.Y == 0d && seek.Z == 0d, Is.True, "A large seek discards old inertia without replay.");
            chin.Advance(20.05d, SpeechMouthPose.Open, true);
            seek = chin.Advance(-1d, SpeechMouthPose.Open, true, 90d, 90d);
            Assert.That(seek.X == 0d && seek.Y == 0d && seek.Z == 0d, Is.True, "A backward seek resets the physical state.");
            Assert.That(chin.IsMoving, Is.False);
        }

        private static void ValidateSpeechFaceSampleLifetime()
        {
            var host = new GameObject("Shared speech face contract");
            try
            {
                var head = new GameObject("Face contract head");
                head.transform.SetParent(host.transform, false);
                var bubbles = host.AddComponent<NpcSpeechBubbleView>();
                bubbles.UseManualClock = true;
                bubbles.Initialize(null);
                Assert.That(bubbles.DeclareSpeaker(head, head.transform, NpcVoiceCatalog.HeroMutterDesignId,
                    NpcEarshotProfile.Conversation), Is.True);
                const string text = "Хочу.";
                Assert.That(bubbles.TryGetSpeechFaceSample(head, out _), Is.False);
                Assert.That(bubbles.ShowAt(head, text, 50f, 3f), Is.True);
                bubbles.AdvanceTo(50.1f);
                Assert.That(bubbles.TryGetSpeechFaceSample(head, out SpeechFaceSample speaking), Is.True);
                Assert.That(speaking.Text, Is.SameAs(text), "The snapshot borrows the original line without substring allocations.");
                Assert.That(speaking.RevealedCharacters, Is.EqualTo(bubbles.RevealedTextOf(head).Length));
                Assert.That(speaking.CurrentCharacter, Is.EqualTo(text[speaking.RevealedCharacters - 1]));
                Assert.That(speaking.IsTyping, Is.True);
                Assert.That(speaking.ElapsedSeconds, Is.EqualTo(.1d).Within(.00001d));
                Assert.That(bubbles.TryGetSpeechFaceSample(head, out SpeechFaceSample paused), Is.True);
                Assert.That(paused.LineToken, Is.EqualTo(speaking.LineToken));
                Assert.That(paused.ElapsedSeconds, Is.EqualTo(speaking.ElapsedSeconds));
                bubbles.AdvanceTo(50.3f);
                Assert.That(bubbles.TryGetSpeechFaceSample(head, out SpeechFaceSample tail), Is.True);
                Assert.That(tail.IsTyping, Is.False, "A living bubble's reading tail is no longer mouth delivery.");
                Assert.That(bubbles.Dismiss(head), Is.True);
                Assert.That(bubbles.TryGetSpeechFaceSample(head, out _), Is.False);
                Assert.That(bubbles.ShowAt(head, text, 51f, 3f), Is.True);
                Assert.That(bubbles.TryGetSpeechFaceSample(head, out SpeechFaceSample next), Is.True);
                Assert.That(next.LineToken, Is.Not.EqualTo(speaking.LineToken));
                Assert.That(next.LineToken, Is.Not.Zero);
                Assert.That(next.RevealedCharacters, Is.Zero);
                head.SetActive(false);
                Assert.That(bubbles.TryGetSpeechFaceSample(head, out _), Is.False,
                    "A lost head is rejected immediately even before the owning clock advances again.");
                bubbles.DismissAll();
            }
            finally { Object.DestroyImmediate(host); }
        }
    }
}
