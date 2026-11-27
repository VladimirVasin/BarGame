using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class DefaultNpcFactoryPlayModeTests
    {
        [UnityTest]
        public IEnumerator DirectBeams_ShieldEyesWithOpenPalmAndReleaseBehindCover()
        {
            host = new GameObject("Glare pose review");
            host.transform.position = new Vector3(70f, 2f, 70f);
            var resident = DefaultNpcFactory.CreateForCharacter(host.transform, DefaultNpcPopulation.Severin);
            CityPortCrew.AlignWorkerModelWithPlacement(resident);
            var reaction = resident.GetComponentInChildren<NpcGlareReaction>();
            var hands = resident.GetComponent<NpcHandPose>();
            Transform Find(string bone) => NpcAttentionHeadLayer.FindBone(resident.ModelRoot, bone);
            var mouth = Find("SOCKET_Mouth");
            var hand = Find("hand.R");
            var leftHand = Find("hand.L");
            var foot = Find("foot.L");
            Vector3 Eyes() => (Find("face.eye.L").position + Find("face.eye.R").position) * .5f;
            var hero = new GameObject("Lamp wearer");
            hero.transform.SetParent(host.transform, false);
            hero.transform.localPosition = new Vector3(12f, 0f, 0f);
            var registry = Player3DResources.Instantiate(hero.transform);
            var body = hero.AddComponent<Player3DCharacterPresentation>();
            body.Initialize(hero.transform, registry);
            var lamp = hero.AddComponent<CombatChestFlashlight>();
            lamp.Initialize(body);
            yield return null;

            Vector3 eyes = Eyes(), forward = mouth.up, right = Vector3.Cross(Vector3.up, forward).normalized;
            Quaternion neutral = resident.Head.localRotation, wrist = hand.localRotation;
            Vector3 planted = foot.position;
            Vector3 origin = eyes + forward * 2.4f + right * .70f;
            void Aim(Vector3 position, Vector3 direction)
            {
                lamp.Lens.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
                Physics.SyncTransforms();
            }
            void Advance()
            {
                for (int i = 0; i < 33; i++) reaction.Advance(1f / 60f);
            }
            void CheckRelease()
            {
                Assert.That(Quaternion.Angle(resident.Head.localRotation, neutral), Is.LessThan(.1f), "Head release");
                Assert.That(Quaternion.Angle(hand.localRotation, wrist), Is.LessThan(.1f), "Wrist release");
            }
            Aim(origin, eyes - origin);
            Advance();
            var frame = hands.GetAnatomicalFrame(false);
            Vector3 palm = hand.position + hand.rotation * frame.PalmCentreInHand;
            Vector3 towardLight = (origin - Eyes()).normalized;
            Assert.That(Vector3.Dot(hands.AnatomicalPalmNormal(false), towardLight), Is.GreaterThan(.97f));
            Assert.That(Vector3.Dot(palm - Eyes(), towardLight), Is.InRange(.075f, .25f));
            Assert.That(Vector3.Cross(palm - Eyes(), towardLight).magnitude, Is.LessThan(.055f));
            Assert.That(Vector3.Dot(mouth.up, right), Is.LessThan(-.35f), "Face turns away from the right-hand light");
            Assert.That(hands.RightGripWeight, Is.LessThanOrEqualTo(.01f));
            Assert.That(Vector3.Distance(planted, foot.position), Is.LessThan(.001f));
            void Capture(string shot)
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BARGAME_GLARE_CAPTURE"))) return;
                var camera = new GameObject("Glare review camera").AddComponent<Camera>();
                camera.transform.SetParent(host.transform, false);
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.19f, .22f, .22f);
                camera.fieldOfView = 35f;
                camera.transform.position = eyes + forward * 1.65f - right * .45f;
                camera.transform.LookAt(eyes - Vector3.up * .15f);
                AreaCaptureFixture.CaptureCurrentCamera(camera, "NpcGlare", shot, 760, 570);
            }
            Capture("flashlight");
            var wall = new GameObject("Opaque screen").AddComponent<BoxCollider>();
            wall.transform.SetParent(host.transform, false);
            wall.transform.SetPositionAndRotation((origin + eyes) * .5f, Quaternion.LookRotation(forward));
            wall.size = new Vector3(1f, 1f, .1f);
            Physics.SyncTransforms();
            Advance();
            CheckRelease();
            wall.enabled = false;
            Aim(origin, Vector3.down);
            Advance();
            CheckRelease();
            Aim(eyes - forward * 2f, forward);
            Advance();
            CheckRelease();
            lamp.enabled = false;
            hero.SetActive(false);

            var car = LastRouteCarFactory.Create(host.transform, LastRouteCarPlan.At(
                resident.transform.position + forward * 8f, -forward), default, null, LastRouteCarLamps.AlwaysDipped);
            var beams = car.GetComponentInParent<LastRouteCarHeadlights>();
            beams.SetPower(1f);
            hands.SetGrip(false, .9f);
            Quaternion leftBefore = leftHand.localRotation;
            Physics.SyncTransforms();
            Advance();
            Assert.That(Quaternion.Angle(neutral, resident.Head.localRotation), Is.GreaterThan(30f), "Real headlights");
            Assert.That(Quaternion.Angle(leftBefore, leftHand.localRotation), Is.GreaterThan(30f), "Free left palm");
            Assert.That(hands.RightGripWeight, Is.EqualTo(.9f).Within(.001f));
            Capture("headlights");
            reaction.enabled = false;
            CheckRelease();
            Assert.That(Quaternion.Angle(leftBefore, leftHand.localRotation), Is.LessThan(.1f), "Pool release");
            reaction.enabled = true;
            Advance();
            beams.SetPower(0f);
            Advance();
            CheckRelease();
            Assert.That(Quaternion.Angle(leftBefore, leftHand.localRotation), Is.LessThan(.1f));
        }
    }
}
