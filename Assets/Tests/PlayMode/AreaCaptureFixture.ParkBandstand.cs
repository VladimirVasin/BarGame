using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class ParkAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.CityMiscAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        /// <summary>
        /// The old chess position now carries the gazebo, both games and
        /// their seated men. Prove the visible floor, actual entrance and
        /// both complete hero seat transfers, then inspect day/night/rain.
        /// </summary>
        [UnityTest]
        [Explicit("Focused park: supported gazebo/games, real entry and both seats, beam lamp, roof shelter and day/night/rain frames.")]
        [PrebuildSetup(typeof(ParkAssetsSetup))]
        public IEnumerator CityParkBandstand()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime((float)(360f / GameTimeState.GameMinutesPerRealSecond));
            CityGameRoot city = null;
            CityDecorationDescriptor stand = default;
            yield return Capture(SceneIds.City, () =>
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                return city != null && city.IsInitialized && !CompositionDriver.IsComposing ? city : null;
            }, () =>
            {
                Assert.That(CityParkGazeboGeometry.TryFind(city.World.DecorationPlan, out stand), Is.True);
                Vector3 origin = stand.Position;
                Vector3 forward = CityParkGazeboGeometry.Forward(stand);
                Vector3 tangent = new Vector3(-forward.z, 0f, forward.x);
                AssertBandstandPennantHangsUnderItsEave(city, origin, forward, tangent);
                AssertBandstandProxiesStayInsideItsPlinth(city, stand, origin, forward, tangent);
                city.Player.Motor.SetInputEnabled(false);
                city.Player.Motor.Teleport(origin - forward * 11f);
                var shots = new List<Shot>
                {
                    Shot.At("park-00-railed-side", origin - forward * 10.5f + Vector3.up * EyeHeight,
                        origin + Vector3.up * 2.1f, 60f),
                    Shot.At("park-01-from-the-path", origin + forward * 10f + tangent * 5.5f + Vector3.up * EyeHeight,
                        origin + Vector3.up * 1.4f, 60f),
                    Shot.At("park-02-floor-and-tables", origin + forward * 5.8f + Vector3.up * 1.35f,
                        origin + Vector3.up * 1.1f, 64f),
                    Shot.At("park-03-beam-and-pennant", origin + tangent * 1.2f + Vector3.up * 1.7f,
                        origin + Vector3.up * 3.9f, 70f),
                };
                AppendParkAuditShots(city, shots);
                return shots.ToArray();
            });

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool followEnabled = follow != null && follow.enabled;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float fov = camera.fieldOfView, aspect = camera.aspect;
            float previousDelta = Time.captureDeltaTime;
            bool weatherEnabled = city.Weather.enabled;
            float rainIntensity = city.Rain.Intensity;
            var externalOwner = new object();
            CityBenchSitInteraction activeSeat = null;
            try
            {
                Time.captureDeltaTime = .05f;
                if (follow != null) follow.enabled = false;
                camera.aspect = (float)Width / Height;
                Physics.SyncTransforms();
                AssertParkFloorAndOccupants(city, stand);
                AssertParkLampAndShelter(city, stand);
                AssertAndCaptureParkFurniture(city, camera);
                Vector3 entry = CityParkGazeboGeometry.ToWorld(stand, new Vector3(0f, 0f, 5.6f));
                Assert.That(Physics.Raycast(entry + Vector3.up * 2f, Vector3.down,
                    out RaycastHit entryGround, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
                entry.y = entryGround.point.y;
                city.Player.Motor.Teleport(entry + Vector3.up * PlayerFactory.GroundedRootOffset);
                for (int frame = 0; frame < 3; frame++) yield return null;
                yield return WalkParkLeg(city, CityParkGazeboGeometry.ToWorld(stand,
                    new Vector3(0f, CityParkGazeboGeometry.DeckHeightMeters, 1.7f)), "up the visible steps");

                IReadOnlyList<CityBoardGameTable> tables = CityBoardGamePlan.Create(city.Layout, city.World.DecorationPlan);
                Assert.That(tables.Count, Is.EqualTo(2));
                foreach (CityBoardGameTable table in tables)
                {
                    activeSeat = FindParkSeat(city, table.SeatId);
                    Assert.That(activeSeat, Is.Not.Null);
                    yield return WalkParkLeg(city, activeSeat.Plan.EntryRootPosition - Vector3.up * PlayerFactory.GroundedRootOffset,
                        table.Game + " side dock");
                    city.Player.Motor.SetInputEnabled(true);
                    city.Player.Interactor.SetInputEnabled(true);
                    Assert.That(activeSeat.CanInteract(city.Player.Interactor), Is.True);
                    Assert.That(activeSeat.Controller.BeginExternalClock(externalOwner), Is.True);
                    activeSeat.Interact(city.Player.Interactor);
                    for (int frame = 0; frame < 160 && !activeSeat.IsSeated; frame++)
                    {
                        activeSeat.Controller.AdvanceExternal(externalOwner, .1f);
                        yield return null;
                    }
                    Assert.That(activeSeat.IsSeated, Is.True, table.Game + " must settle on the raised plank.");
                    Assert.That(city.BoardGames.IsPlaying, Is.True);
                    Assert.That(city.BoardGames.ActiveGame, Is.EqualTo(table.Game));
                    var hero = city.Player.Visual as Player3DCharacterPresentation;
                    Assert.That(hero, Is.Not.Null);
                    hero.ReapplyLatePresentationPose();
                    activeSeat.Controller.RefreshActiveClipAlignment();
                    Assert.That(Vector3.Distance(hero.Registry.Anchors.Pelvis.position, activeSeat.Plan.ActionHipPosition),
                        Is.LessThan(.035f), "The same hero must meet the real plank on the raised deck.");
                    Vector3 view = table.SeatTopCenter - table.SeatFacing * 2.8f + table.Tangent * 1.6f + Vector3.up * .8f;
                    ParkFrame(camera, "park-04-seated-" + table.Game, view, table.BoardCenter + Vector3.up * .1f, 65f);
                    Assert.That(activeSeat.RequestExit(), Is.True);
                    for (int frame = 0; frame < 160 && activeSeat.OwnsActiveInteraction; frame++)
                    {
                        activeSeat.Controller.AdvanceExternal(externalOwner, .1f);
                        yield return null;
                    }
                    Assert.That(activeSeat.OwnsActiveInteraction, Is.False);
                    Assert.That(city.BoardGames.IsPlaying, Is.False);
                    activeSeat.Controller.ReleaseExternalClock(externalOwner);
                    Assert.That(city.Player.Motor.transform.position.y,
                        Is.EqualTo(activeSeat.Plan.EntryRootPosition.y).Within(.04f));
                    activeSeat = null;
                }
                yield return WalkParkLeg(city, CityParkGazeboGeometry.ToWorld(stand,
                    new Vector3(0f, CityParkGazeboGeometry.DeckHeightMeters, 1.7f)), "back to the central entrance");
                yield return WalkParkLeg(city, entry, "down the visible steps");

                Vector3 origin = stand.Position;
                Vector3 forward = CityParkGazeboGeometry.Forward(stand);
                Vector3 tangent = new Vector3(-forward.z, 0f, forward.x);
                city.Player.Motor.Teleport(origin + forward * 7f + Vector3.up * PlayerFactory.GroundedRootOffset);
                GameSessionState.AdvanceGameTime((float)((21d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                    GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                for (int frame = 0; frame < SettleFrames; frame++) yield return null;
                ParkFrame(camera, "park-05-night", origin + forward * 9f + tangent * 4.5f + Vector3.up * EyeHeight,
                    origin + Vector3.up * 1.5f, 62f);
                city.Weather.enabled = false;
                city.Rain.SetIntensity(1f);
                GameSessionState.AdvanceGameTime((float)((15d * 60d + 24d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                    GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                for (int frame = 0; frame < 30; frame++) yield return null;
                ParkFrame(camera, "park-06-rain", origin + forward * 6.4f + tangent * .6f + Vector3.up * EyeHeight,
                    origin + Vector3.up * 1.6f, 68f);
                AssertParkLampAndShelter(city, stand);
                Debug.Log("PARK BANDSTAND ACCEPTANCE OK: supported games/men, actual steps and both complete hero seat transfers, beam lamp, roof shelter and day/night/rain views.");
            }
            finally
            {
                if (activeSeat != null)
                {
                    activeSeat.Controller.CancelActiveInteraction();
                    activeSeat.Controller.ReleaseExternalClock(externalOwner);
                }
                city.Player.Motor.CancelInteractionPoseMove();
                city.Player.Motor.SetInputEnabled(true);
                city.Weather.enabled = weatherEnabled;
                city.Rain.SetIntensity(rainIntensity);
                Time.captureDeltaTime = previousDelta;
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                camera.fieldOfView = fov; camera.aspect = aspect;
                if (follow != null) follow.enabled = followEnabled;
            }
        }

        private static void AppendParkAuditShots(CityGameRoot city, List<Shot> shots)
        {
            bool fountain = false, playground = false;
            foreach (CityDecorationDescriptor descriptor in city.World.DecorationPlan.Descriptors)
            {
                if (descriptor.Kind != CityDecorationKind.ParkFountainAndStatue &&
                    descriptor.Kind != CityDecorationKind.ParkPlayground) continue;
                Assert.That(CityDecorationWorldBuilder.TryDescribeRecipeBasis(city.Layout,
                    descriptor, out Vector3 origin, out Vector3 forward), Is.True);
                Vector3 tangent = new Vector3(-forward.z, 0f, forward.x);
                if (descriptor.Kind == CityDecorationKind.ParkFountainAndStatue)
                {
                    fountain = true;
                    shots.Add(Shot.At("park-audit-fountain-near-human-figure",
                        origin + forward * 5.5f + tangent * 1.3f + Vector3.up * EyeHeight,
                        origin + Vector3.up * 2.65f, 54f));
                    Vector3 fromPath = ParkAuditPathPoint(city.Layout, origin, 16f);
                    shots.Add(Shot.At("park-audit-fountain-path-silhouette",
                        fromPath + Vector3.up * EyeHeight, origin + Vector3.up * 2.2f, 58f));
                }
                else
                {
                    playground = true;
                    shots.Add(Shot.At("park-audit-playground-and-swing-supports",
                        origin + forward * 7f + tangent * 3.5f + Vector3.up * EyeHeight,
                        origin + Vector3.up * 1.15f, 64f));
                }
            }
            Assert.That(fountain && playground, Is.True, "The existing public park landmarks must remain available for visual review.");
            foreach (CityParkRegionPlan region in city.Layout.Park.Regions)
            {
                Vector3 fromPath = ParkAuditPathPoint(city.Layout, region.Center, 10f);
                shots.Add(Shot.At("park-audit-trees-paths-" + region.Id,
                    fromPath + Vector3.up * EyeHeight, region.Center + Vector3.up * 1.6f, 68f));
            }
            foreach (CityParkGateDescriptor gate in city.Layout.Park.Gates)
            {
                Vector3 outside = gate.Center + gate.OutwardNormal * 5f;
                if (CityTerrainSurfacePlan.TrySampleGroundTop(city.Layout,
                    new Vector2(outside.x, outside.z), out float top, out _)) outside.y = top;
                shots.Add(Shot.At("park-audit-gate-" + gate.Id,
                    outside + Vector3.up * EyeHeight,
                    gate.Center - gate.OutwardNormal * 6f + Vector3.up * 1.6f, 66f));
            }
        }

        private static Vector3 ParkAuditPathPoint(CityLayout layout, Vector3 target, float reach)
        {
            Vector3 best = target + Vector3.forward * reach;
            float bestError = float.PositiveInfinity;
            foreach (RoadEdge edge in layout.RoadEdges)
            {
                if (layout.GetPathKind(edge) != CityPathKind.ParkPath) continue;
                Vector2 center = layout.GetRoadRect(edge).center;
                Vector3 candidate = new Vector3(center.x, target.y, center.y);
                float distance = Vector2.Distance(center, new Vector2(target.x, target.z));
                float error = Mathf.Abs(distance - reach);
                if (distance < 5f || error >= bestError) continue;
                best = candidate; bestError = error;
            }
            if (CityTerrainSurfacePlan.TrySampleGroundTop(layout,
                new Vector2(best.x, best.z), out float ground, out _)) best.y = ground;
            return best;
        }

        private static IEnumerator WalkParkLeg(CityGameRoot city, Vector3 surfacePoint, string label)
        {
            PlayerMotor motor = city.Player.Motor;
            motor.SetInputEnabled(false);
            Vector3 target = surfacePoint + Vector3.up * PlayerFactory.GroundedRootOffset;
            bool arrived = false;
            for (int frame = 0; frame < 180 && !arrived; frame++)
            {
                arrived = motor.MoveTowardsApproachWaypoint(target, .04f, .05f);
                if (motor.InteractionPoseMoveStalled)
                    Assert.Fail(DescribeParkWalkFailure(city, target, label, "stalled"));
                yield return null;
            }
            motor.CancelInteractionPoseMove();
            if (!arrived) Assert.Fail(DescribeParkWalkFailure(city, target, label, "did not arrive"));
            if (Mathf.Abs(motor.transform.position.y - target.y) > .08f)
                Assert.Fail(DescribeParkWalkFailure(city, target, label, "wrong floor height"));
        }

        private static string DescribeParkWalkFailure(CityGameRoot city, Vector3 target, string label, string reason)
        {
            Physics.SyncTransforms();
            Transform hero = city.Player.GameObject.transform;
            Vector3 current = hero.position;
            Vector3 direction = Vector3.ProjectOnPlane(target - current, Vector3.up).normalized;
            if (direction.sqrMagnitude < .001f) direction = hero.forward;
            string basis = "gazebo absent";
            if (CityParkGazeboGeometry.TryFind(city.World.DecorationPlan, out CityDecorationDescriptor gazebo))
            {
                Vector3 forward = CityParkGazeboGeometry.Forward(gazebo);
                Vector3 tangent = new Vector3(-forward.z, 0f, forward.x);
                Vector3 here = current - gazebo.Position, there = target - gazebo.Position;
                basis = $"base={gazebo.Position:F3}, forward={forward:F3}, " +
                    $"currentLocal=({Vector3.Dot(here, tangent):F3},{here.y:F3},{Vector3.Dot(here, forward):F3}), " +
                    $"targetLocal=({Vector3.Dot(there, tangent):F3},{there.y:F3},{Vector3.Dot(there, forward):F3})";
            }
            string ground = "none";
            float nearest = float.PositiveInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(current + Vector3.up * 2.1f,
                Vector3.down, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!hit.collider.transform.IsChildOf(hero) && hit.distance < nearest)
                {
                    nearest = hit.distance;
                    ground = $"{hit.collider.name} at {hit.point:F3}, normal={hit.normal:F3}";
                }
            CharacterController capsule = hero.GetComponent<CharacterController>();
            Vector3 center = hero.TransformPoint(capsule.center);
            float radius = capsule.radius * Mathf.Max(hero.lossyScale.x, hero.lossyScale.z);
            float half = Mathf.Max(0f, capsule.height * hero.lossyScale.y * .5f - radius);
            var obstacles = new List<string>();
            foreach (RaycastHit hit in Physics.CapsuleCastAll(center - Vector3.up * half,
                center + Vector3.up * half, radius, direction, .7f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!hit.collider.transform.IsChildOf(hero))
                    obstacles.Add($"{hit.collider.name} distance={hit.distance:F3} point={hit.point:F3} normal={hit.normal:F3}");
            string message = $"PARK WALK {label}: {reason}; current={current:F3}, target={target:F3}; {basis}; " +
                $"downray={ground}; capsule .7m forward=[{string.Join("; ", obstacles)}]";
            Debug.Log(message);
            try
            {
                Vector3 side = new Vector3(direction.z, 0f, -direction.x);
                ParkFrame(Camera.main, "park-failed-walk-" + label.Replace(" ", "-"),
                    current - direction * 3.5f + side * 2f + Vector3.up * 1.8f,
                    current + direction * .8f + Vector3.up * .7f, 66f);
            }
            catch (Exception error) { Debug.Log("Park failure capture: " + error.Message); }
            return message;
        }

        private static CityBenchSitInteraction FindParkSeat(CityGameRoot city, string id)
        {
            foreach (CityBenchSitInteraction seat in city.GetComponentsInChildren<CityBenchSitInteraction>())
                if (seat.Plan.Id == id) return seat;
            return null;
        }

        private static void AssertParkFloorAndOccupants(CityGameRoot city, CityDecorationDescriptor stand)
        {
            float deckY = stand.Position.y + CityParkGazeboGeometry.DeckHeightMeters;
            IReadOnlyList<CityBoardGameTable> tables = CityBoardGamePlan.Create(city.Layout, city.World.DecorationPlan);
            Assert.That(tables.Count, Is.EqualTo(2));
            foreach (CityBoardGameTable table in tables)
            {
                Assert.That(table.Origin.y, Is.EqualTo(deckY).Within(.002f));
                Assert.That(table.SeatTopCenter.y,
                    Is.EqualTo(deckY + CityChessBoardGeometry.BenchSeatTopY).Within(.002f));
                CityBenchSitInteraction seat = FindParkSeat(city, table.SeatId);
                Assert.That(seat, Is.Not.Null);
                Assert.That(seat.Plan.EntryRootPosition.y,
                    Is.EqualTo(deckY + PlayerFactory.GroundedRootOffset).Within(.002f));
                AssertParkFloorHit(city.World.Gazebo, stand, seat.Plan.EntryRootPosition, deckY, table.Game + " entry dock");
            }
            Assert.That(city.ParkChessPlayer.SeatPelvisY,
                Is.EqualTo(deckY + CityChessBoardGeometry.BenchSeatTopY + ParkChessPlayerPresentation.PerchPelvisLiftMeters).Within(.002f));
            Assert.That(city.ParkCheckersPlayer.SeatPelvisY,
                Is.EqualTo(deckY + CityChessBoardGeometry.BenchSeatTopY + ParkCheckersPlayerPresentation.PerchPelvisLiftMeters).Within(.002f));
            foreach (Vector3 local in new[] { new Vector3(0, .54f, 0), new Vector3(0, .54f, 3.5f),
                new Vector3(0, .36f, 4.1f), new Vector3(0, .18f, 4.65f) })
            {
                Vector3 expected = CityParkGazeboGeometry.ToWorld(stand, local);
                AssertParkFloorHit(city.World.Gazebo, stand, expected, expected.y, "deck/visible step");
            }
        }

        private static void AssertParkFloorHit(CityParkGazeboWorld gazebo, CityDecorationDescriptor stand, Vector3 point, float expectedY, string label)
        {
            Assert.That(CityParkGazeboGeometry.TrySampleFloorTop(stand, point, out float planned), Is.True, label);
            Assert.That(planned, Is.EqualTo(expectedY).Within(.003f), label);
            Assert.That(Physics.Raycast(new Vector3(point.x, expectedY + .35f, point.z), Vector3.down,
                out RaycastHit hit, 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True, label);
            Assert.That(hit.point.y, Is.EqualTo(expectedY).Within(.025f), label + " actual collider");
            Assert.That(gazebo, Is.Not.Null);
            Assert.That(hit.collider.transform.IsChildOf(gazebo.transform), Is.True,
                label + " must stand on the imported visible gazebo, not unrelated ground.");
        }

        private static void AssertParkLampAndShelter(CityGameRoot city, CityDecorationDescriptor stand)
        {
            foreach (StreetLampDescriptor streetLamp in CityNightFixturePlanner.CreatePlan(city.Layout).StreetLamps)
                Assert.That(city.Layout.GetPathKind(streetLamp.Edge), Is.Not.EqualTo(CityPathKind.ParkPath),
                    "Park paths retain darkness between gates; only the gazebo pendant lights the park.");
            CityParkChessLampPlan lamp = CityParkChessLampPlan.Create(city.Layout, city.World.DecorationPlan);
            Assert.That(lamp.IsPresent, Is.True);
            Assert.That(lamp.LitPendant.IsLit && !lamp.DeadPendant.IsLit, Is.True);
            foreach (CityParkChessLampPendant pendant in new[] { lamp.LitPendant, lamp.DeadPendant })
            {
                Assert.That(pendant.MountPoint.y - stand.Position.y,
                    Is.EqualTo(CityParkGazeboGeometry.BeamUndersideHeightMeters).Within(.002f));
                Vector3 local = pendant.MountPoint - stand.Position;
                Assert.That(Mathf.Abs(Vector3.Dot(local, lamp.Tangent)),
                    Is.EqualTo(CityChessBoardGeometry.TableOffsetMeters).Within(.002f));
                Assert.That(Vector3.Dot(local, lamp.Forward), Is.EqualTo(0f).Within(.002f),
                    "Each pendant mounts to the real cross-beam above its table.");
            }
            Assert.That(city.ParkChessLamp.GetComponentsInChildren<Light>(true).Length, Is.EqualTo(1));
            bool found = false;
            foreach (Collider shelter in city.Rain.LocalShelters)
            {
                if (shelter.name != "Gazebo Roof Shelter") continue;
                found = true;
                Assert.That(shelter.enabled && shelter.isTrigger, Is.True);
                foreach (CityBoardGameTable table in CityBoardGamePlan.Create(city.Layout, city.World.DecorationPlan))
                {
                    Vector3 probe = table.BoardCenter;
                    probe.y = shelter.bounds.center.y;
                    Assert.That(shelter.bounds.Contains(probe), Is.True, "Both boards must sit below the registered roof.");
                }
            }
            Assert.That(found, Is.True, "The actual rain field must register the gazebo roof.");
            Assert.That(city.Rain.Particles.trigger.enabled, Is.True);
            Assert.That(city.Rain.Particles.trigger.inside, Is.EqualTo(ParticleSystemOverlapAction.Kill));
        }

        private static void AssertAndCaptureParkFurniture(CityGameRoot city, Camera camera)
        {
            CityParkFurniturePlan plan = CityParkFurniturePlan.Create(city.Layout, city.World.DecorationPlan);
            Assert.That(plan.Placements.Count, Is.LessThanOrEqualTo(CityParkFurniturePlan.MaximumPlacementCount));
            Transform root = FindParkPart(city.World.Root.transform, CityParkFurnitureWorldBuilder.RootName);
            Assert.That(root, Is.Not.Null);
            int bins = 0, notices = 0;
            foreach (CityParkFurniturePlacement placement in plan.Placements)
            {
                Transform prop = root.Find(placement.Id);
                Assert.That(prop, Is.Not.Null, placement.Id);
                Assert.That(Vector3.Distance(prop.position, placement.Position), Is.LessThan(.002f));
                Assert.That(prop.lossyScale, Is.EqualTo(Vector3.one));
                BoxCollider[] collision = prop.GetComponents<BoxCollider>();
                Assert.That(collision.Length, Is.EqualTo(placement.Kind == CityMiscKind.ParkBin ? 1 : 3));
                foreach (BoxCollider collider in collision)
                    Assert.That(collider.enabled && !collider.isTrigger, Is.True);
                if (placement.Kind == CityMiscKind.ParkBin) bins++;
                else if (placement.Kind == CityMiscKind.ParkNoticeBoard) notices++;
                Vector3 forward = placement.Rotation * Vector3.forward;
                Vector3 right = placement.Rotation * Vector3.right;
                ParkFrame(camera, "park-07-" + placement.Id,
                    placement.Position + forward * 3.6f + right * 1.4f + Vector3.up * 1.5f,
                    placement.Position + Vector3.up * (placement.Kind == CityMiscKind.ParkBin ? .55f : 1.25f), 58f);
            }
            Assert.That(bins, Is.EqualTo(CityParkFurniturePlan.MaximumBinCount), "The production park has a legal bin beside each half's benches.");
            Assert.That(notices, Is.EqualTo(1), "The production park has one blank notice board near its public path.");
            Transform repaired = FindParkPart(city.World.Root.transform, "Park Bench Replacement Boards");
            Assert.That(repaired, Is.Not.Null);
            Assert.That(repaired.GetComponent<MeshRenderer>(), Is.Not.Null);
            CityParkBenchDescriptor bench = city.Layout.Park.Benches[1];
            ParkFrame(camera, "park-08-repaired-bench",
                bench.Position + bench.Forward * 3.1f + bench.Tangent * 1.6f + Vector3.up * 1.3f,
                bench.Position + Vector3.up * .55f, 56f);
        }

        private static Transform FindParkPart(Transform parent, string name)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        private static void ParkFrame(Camera camera, string name, Vector3 position, Vector3 target, float fov)
        {
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            camera.fieldOfView = fov;
            var texture = new RenderTexture(Width, Height, 24);
            var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                pixels.Apply();
                Assert.That(IsBlank(pixels), Is.False, name);
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "City");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(pixels);
                texture.Release(); Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// The cloth's outer edge must fit inside the actual twelve-sided
        /// eave, not merely its enclosing ellipse; the pin alone is too lax.
        /// </summary>
        private static void AssertBandstandPennantHangsUnderItsEave(
            CityGameRoot city,
            Vector3 origin,
            Vector3 forward,
            Vector3 tangent)
        {
            CityWindDressingPlan wind = city.World.WindDressingPlan;
            Assert.That(wind, Is.Not.Null, "The park owes the city its wind dressing.");

            var pennant = default(CityWindDressingClothDescriptor);
            bool found = false;
            for (int index = 0; index < wind.Cloths.Count; index++)
            {
                if (wind.Cloths[index].Kind !=
                    CityWindDressingKind.BandstandPennant)
                {
                    continue;
                }

                pennant = wind.Cloths[index];
                found = true;
                break;
            }

            Assert.That(found, Is.True,
                "The bandstand owns the park's one remnant pennant.");

            Vector3 offset = pennant.Position - origin;
            float across = Vector3.Dot(offset, tangent);
            float along = Vector3.Dot(offset, forward);
            float half = pennant.Width * 0.5f;
            foreach (float edge in new[] { across - half, across, across + half })
            {
                float seated = EaveRingFraction(edge, along);
                Assert.That(seated, Is.LessThan(1f),
                    $"The pennant reaches {seated:F3} of the way out of an " +
                    "eave that ends at 1.000; it hangs past the roof, in " +
                    "the air.");
            }

            Assert.That(offset.y, Is.InRange(3.9f, 4.1f),
                "The pennant pins to the eave underside.");
        }

        /// <summary>
        /// Routing blockers describe columns and rear rails. Their corners
        /// stay over visible stone and never invent a wall outside the gazebo.
        /// WalkParkLeg separately proves the real entrance and occupied deck.
        /// </summary>
        private static void AssertBandstandProxiesStayInsideItsPlinth(
            CityGameRoot city,
            CityDecorationDescriptor stand,
            Vector3 origin,
            Vector3 forward,
            Vector3 tangent)
        {
            var proxies = new List<RuntimeOrientedBox>();
            CityStaticCollisionBuilder.AddDecorationProxyBoxes(city.Layout, stand, proxies);
            Assert.That(proxies, Is.Not.Empty, "The gazebo has real columns and rear rails.");
            float widest = 0f;
            foreach (RuntimeOrientedBox proxy in proxies)
                foreach (int acrossSign in new[] { -1, 1 })
                    foreach (int heightSign in new[] { -1, 1 })
                        foreach (int alongSign in new[] { -1, 1 })
                        {
                            Vector3 corner = proxy.Center + proxy.Rotation * new Vector3(
                                acrossSign * proxy.Size.x * .5f,
                                heightSign * proxy.Size.y * .5f,
                                alongSign * proxy.Size.z * .5f);
                            Vector3 offset = corner - origin;
                            float seated = PlinthRingFraction(Vector3.Dot(offset, tangent),
                                Vector3.Dot(offset, forward));
                            widest = Mathf.Max(widest, seated);
                            Assert.That(seated, Is.LessThan(1.001f),
                                "A real rotated column/rail corner exceeds the visible plinth.");
                        }

            Debug.Log(
                $"BANDSTAND PROXIES: {proxies.Count} boxes, " +
                $"furthest corner at {widest:F3} of the plinth.");
        }

        private static float PlinthRingFraction(float across, float along)
        {
            return RingFraction(across, along, CityParkGazeboGeometry.PlinthRadiusX, CityParkGazeboGeometry.PlinthRadiusZ);
        }

        /// <summary>
        /// How far out a point lies on the eave's twelve-gon, where `1.0` is
        /// its boundary: the distance to the point over the distance from the
        /// centre to the ring's edge in the same direction.
        /// </summary>
        private static float EaveRingFraction(float across, float along)
        {
            return RingFraction(across, along, CityParkGazeboGeometry.EaveRadiusX, CityParkGazeboGeometry.EaveRadiusZ);
        }

        private static float RingFraction(
            float across,
            float along,
            float Across,
            float Along)
        {
            const int Sides = 12;

            float distance = Mathf.Sqrt((across * across) + (along * along));
            if (distance <= Mathf.Epsilon)
            {
                return 0f;
            }

            float angle = Mathf.Atan2(along, across);
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            for (int side = 0; side < Sides; side++)
            {
                float first = side * Mathf.PI * 2f / Sides;
                float second = (side + 1) * Mathf.PI * 2f / Sides;
                var a = new Vector2(
                    Across * Mathf.Cos(first), Along * Mathf.Sin(first));
                var b = new Vector2(
                    Across * Mathf.Cos(second), Along * Mathf.Sin(second));
                Vector2 edge = b - a;
                float denominator =
                    (edge.x * direction.y) - (edge.y * direction.x);
                if (Mathf.Abs(denominator) < 1e-9f)
                {
                    continue;
                }

                float t =
                    ((a.y * direction.x) - (a.x * direction.y)) / denominator;
                if (t < -1e-6f || t > 1f + 1e-6f)
                {
                    continue;
                }

                Vector2 hit = a + (edge * t);
                if (Vector2.Dot(hit, direction) > 0f)
                {
                    return distance / hit.magnitude;
                }
            }

            throw new InvalidOperationException(
                "The eave ring has no edge in that direction.");
        }
    }
}
