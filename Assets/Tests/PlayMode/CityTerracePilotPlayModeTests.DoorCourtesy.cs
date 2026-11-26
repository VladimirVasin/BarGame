using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [Test]
        public void DoorCourtesy_ElbowHoldsForNeighbourAndCloserWaits()
        {
            var layout = CityLayoutCache.GetOrGenerate(CityBlueprintCatalog.Default, CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityHillsideBuilding house = null;
            foreach (var entry in CityHillsideNeighborhoodPlan.ForLayout(layout).SharedCourtBuildings)
                if (entry.Lot.Cell == CityCourtDoorCourtesy.Address) house = entry;
            Assert.That(house, Is.Not.Null);
            var stage = new GameObject("Passage court door check").transform;
            try
            {
                CityHillsideNeighborhoodWorld.BuildHouse(stage, layout, house);
                var door = CityCourtDoorCourtesy.Build(stage, house);
                var root = door.transform;
                var sun = new GameObject("Court daylight").AddComponent<Light>();
                sun.transform.SetParent(stage, false);
                sun.type = LightType.Directional; sun.intensity = 1.45f;
                sun.transform.rotation = root.rotation * Quaternion.Euler(45f, 150f, 0f);
                var camera = new GameObject("Forecourt observer").AddComponent<Camera>();
                camera.transform.SetParent(stage, false);
                camera.transform.position = root.TransformPoint(new Vector3(.24f, 1.46f, 2.7f));
                camera.transform.LookAt(root.TransformPoint(new Vector3(0f, 1.08f, -.52f)));
                camera.nearClipPlane = .03f; camera.fieldOfView = 49f;
                var player = new GameObject("Arriving hero").AddComponent<CharacterController>();
                player.transform.SetParent(stage, false);
                player.transform.position = root.TransformPoint(new Vector3(0f, 0f, .2f));
                player.center = Vector3.up * .85f; player.height = 1.7f; player.radius = .26f;
                Physics.SyncTransforms();

                door.Seek(0d);
                Transform exiting = door.ResidentBody(0), arriving = door.ResidentBody(1);
                bool met = false, passed = false, slowClose = false, captured = false;
                const float dt = 1f / 30f;
                for (int frame = 1; frame <= 540; frame++)
                {
                    float now = frame * dt;
                    door.Advance(now, dt, now >= 9f && now <= 14f ? player : null, true, false);
                    if (door.IsHolding)
                    {
                        met = true;
                        Assert.That(door.OpenFraction, Is.GreaterThan(.99f));
                        Assert.That(root.InverseTransformPoint(exiting.position).z, Is.EqualTo(-.49f).Within(.01f));
                        Assert.That(root.InverseTransformPoint(door.HolderElbow.position).x,
                            Is.EqualTo(root.InverseTransformPoint(door.DoorHinge.position).x - .034f).Within(.025f));
                        Assert.That(Vector3.Distance(exiting.position, arriving.position), Is.GreaterThan(.54f));
                        float guestDepth = root.InverseTransformPoint(arriving.position).z;
                        if (!captured && guestDepth < .66f)
                        {
                            AreaCaptureFixture.CaptureCurrentCamera(camera, "CityDoorCourtesy", "held", 960, 640);
                            captured = true;
                        }
                        if (guestDepth < -.65f) passed = true;
                    }
                    if (now >= 10f && now <= 14f) Assert.That(door.OpenFraction, Is.EqualTo(1f).Within(.001f));
                    if (now > 14f && door.OpenFraction > .1f && door.OpenFraction < .9f) slowClose = true;
                }
                Assert.That(met && passed && captured && slowClose, Is.True);
                Assert.That(door.IsHolding, Is.False);
                Assert.That(door.OpenFraction, Is.Zero);
                Assert.That(root.InverseTransformPoint(exiting.position).z, Is.GreaterThan(2f));
                Assert.That(root.InverseTransformPoint(arriving.position).z, Is.LessThan(-1.25f));

                // Later the same incoming resident uses this address alone.
                door.Seek(43d);
                for (int frame = 1; frame <= 360; frame++)
                {
                    door.Advance(43d + frame * dt, dt, null, true, false);
                    Assert.That(door.IsHolding, Is.False);
                }
                Assert.That(door.OpenFraction, Is.Zero);
            }
            finally { Object.DestroyImmediate(stage.gameObject); }
        }
    }
}
