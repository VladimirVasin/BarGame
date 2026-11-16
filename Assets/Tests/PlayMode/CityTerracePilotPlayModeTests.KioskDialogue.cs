using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        public IEnumerator LiveCity_KioskTreatOpensThreeDialoguesWithSubtitles()
        {
            Assert.That(GameSessionState.TryStartGameTimeAt(20 * 60 + 30), Is.True);
            yield return LoadProductionCity();
#if UNITY_EDITOR
            if (!Application.isBatchMode)
            {
                Type viewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView", true);
                var view = UnityEditor.EditorWindow.GetWindow(viewType);
                view.Show();
                view.Focus();
            }
#endif
            var court = city.World.Root.GetComponentInChildren<RidgeKioskCourtLife>();
            Assert.That(court, Is.Not.Null);
            var session = court.TableDialogue;
            var targets = court.GetComponentsInChildren<RidgeKioskTableInteraction>().OrderBy(target => target.Table).ToArray();
            Assert.That(targets.Length, Is.EqualTo(3));
            var player = city.Player.Interactor;
            var foundScreen = WorldItemFoundScreen.For(player);
            var follow = city.Camera.GetComponent<PlayerCameraFollow>();
            var station = court.GetComponentInParent<RidgeKioskStation>();
            // This scenario isolates the dialogue from the already covered
            // refill trip by keeping the window busy. Guests retain their drinks.
            Transform waitingCustomer = player.transform;
            Assert.That(station.TryClaimNpc(waitingCustomer), Is.True);
            var input = new InputTestFixture();
            input.Setup();
            var keys = InputSystem.AddDevice<Keyboard>();
            bool capture = Environment.GetEnvironmentVariable("BAR_PROMENADE_KIOSK_DIALOGUE_CAPTURE") == "1";
            float savedFrame = Time.captureDeltaTime;
            Time.captureDeltaTime = .1f;

            try
            {
                yield return Approach(0);
                yield return Press(keys.eKey);
                Assert.That(foundScreen.IsPresenting || session.IsActive, Is.False);
                Assert.That(court.TableRound(0), Is.EqualTo(-1));
                yield return Until(() => !player.PromptView.IsFeedbackVisible, "missing treat feedback");
                Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.BeerCan, 4), Is.True);
                Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.VodkaBottle, 3), Is.True);
                Assert.That(GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.BeerCan, true), Is.True);
                int opened = GameSessionState.DrinkContainerId(InventoryItemId.BeerCan);
                Assert.That(GameSessionState.SetDrinkOpened(InventoryItemId.BeerCan, opened, true), Is.True);
                Assert.That(GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.BeerCan, false), Is.True);
                for (int frame = 0; frame < 28 && PlayerVodkaBottleController.IsHandOccupied(player.gameObject); frame++)
                    yield return null;
                yield return Approach(0);

                yield return Press(keys.eKey);
                yield return Until(() => foundScreen.IsShowing, "3D treat preview");
                Assert.That(foundScreen.TitleKey, Is.EqualTo("kiosk.dialogue.two_beers"));
                yield return Press(keys.escapeKey);
                yield return Until(() => !foundScreen.IsPresenting && targets[0].CanInteract(player), "cancel before handoff");
                Assert.That(DrinkCount(), Is.EqualTo(7));

                int intoxication = GameSessionState.IntoxicationLevel;
                for (int table = 0; table < targets.Length; table++)
                {
                    yield return Approach(table);
                    int before = DrinkCount();
                    yield return Press(keys.eKey);
                    yield return Until(() => foundScreen.IsShowing, "preview for table " + table);
                    // The opening E only opens the preview.
                    Assert.That(DrinkCount(), Is.EqualTo(before));
                    Assert.That(foundScreen.TitleKey, Is.EqualTo(table == 0 ? "kiosk.dialogue.two_beers" :
                        table == 1 ? "kiosk.dialogue.mixed" : "kiosk.dialogue.two_vodkas"));
                    yield return Press(keys.eKey);
                    yield return Until(() => session.Phase == DialoguePhase.Presenting, "handoff at table " + table);
                    Assert.That(DrinkCount(), Is.EqualTo(before));
                    var pair = city.Pedestrians.Actors.Where(actor => actor.IsSpawned)
                        .Select(actor => (Actor: actor, Id: city.Pedestrians.ResidentIndex(actor)))
                        .Where(member => member.Id >= 0 && city.Pedestrians.Residents.KioskPlace(member.Id) >= 0 &&
                            city.Pedestrians.Residents.KioskPlace(member.Id) / RidgeKioskCourtPlan.PlacesPerTable == table)
                        .OrderBy(member => city.Pedestrians.Residents.KioskPlace(member.Id)).ToArray();
                    Assert.That(pair, Has.Length.EqualTo(2));
                    var oldDrinks = pair.Select(member => member.Actor.GetComponentInChildren<VodkaBottleView>()).ToArray();
                    Assert.That(oldDrinks.All(drink => drink && drink.IsOpened), Is.True);
                    var oldFill = oldDrinks.Select(drink => drink.Remaining).ToArray();
                    var gifts = court.GetComponentsInChildren<VodkaBottleView>(true)
                        .Where(drink => drink.transform.parent == court.transform)
                        .OrderBy(drink => drink.transform.position.x).ToArray();
                    Assert.That(gifts.Select(drink => drink.ItemId), Is.EqualTo(new[] {
                        table == 2 ? InventoryItemId.VodkaBottle : InventoryItemId.BeerCan,
                        table == 0 ? InventoryItemId.BeerCan : InventoryItemId.VodkaBottle }));
                    Assert.That(gifts.All(drink => !drink.IsOpened && drink.Remaining == 1f), Is.True);
                    if (capture && table == 0)
                    {
                        yield return new WaitForSeconds(.75f);
                        yield return Capture("01-treat");
                    }
                    yield return Until(() => DrinkCount() == before - 2, "treat payment at table " + table);
                    float paidAt = Time.time;
                    int released = 0;
                    yield return Until(() =>
                    {
                        for (int side = 0; side < pair.Length; side++)
                        {
                            int bit = 1 << side;
                            if ((released & bit) != 0 || !oldDrinks[side].TryGetComponent(out ThrownVodkaBottle throwaway)) continue;
                            Assert.That(session.Phase, Is.EqualTo(DialoguePhase.Presenting));
                            Assert.That(throwaway.Body != null && !throwaway.Body.isKinematic, Is.True);
                            Assert.That(oldDrinks[side].Remaining, Is.EqualTo(oldFill[side]));
                            Assert.That(gifts[side].transform.parent, Is.EqualTo(court.transform));
                            AssertInShot(throwaway.Body.worldCenterOfMass, "discard at table " + table);
                            released |= bit;
                        }
                        return released == 3;
                    }, "both old props released into physics at table " + table);
                    Assert.That(Time.time - paidAt, Is.LessThan(1.6f));
                    if (capture) yield return Capture("03-discard-" + table);

                    yield return Until(() => gifts[0].transform.IsChildOf(pair[0].Actor.transform) &&
                        gifts[1].transform.IsChildOf(pair[1].Actor.transform), "both gift props held at table " + table);
                    Assert.That(session.Phase, Is.EqualTo(DialoguePhase.Presenting));
                    Assert.That(Time.time - paidAt, Is.LessThan(4f));
                    foreach (var gift in gifts) AssertInShot(gift.transform.position, "pickup at table " + table);
                    if (capture) yield return Capture("04-pickup-" + table);

                    yield return Until(() => session.Phase == DialoguePhase.Speaking, "welcome after pickup at table " + table);
                    for (int side = 0; side < pair.Length; side++)
                    {
                        Assert.That(pair[side].Actor.GetComponentInChildren<VodkaBottleView>(), Is.SameAs(gifts[side]));
                        Assert.That(GameSessionState.KioskGift(table * RidgeKioskCourtPlan.PlacesPerTable + side,
                            DefaultNpcPopulation.PedestrianId(pair[side].Id)), Is.EqualTo(InventoryItemId.None));
                    }
                    yield return Until(() => session.IsChoosing, "first question at table " + table);
                    Assert.That(session.CurrentNode.Id, Is.EqualTo("round1c"));
                    Assert.That(court.TableRound(table), Is.Zero);
                    Assert.That(DrinkCount(), Is.EqualTo(before - 2));
                    Assert.That(GameSessionState.DrinkPreviewOpened(InventoryItemId.BeerCan), Is.True);
                    Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(intoxication));
                    Assert.That(session.Bubbles.UseSubtitles, Is.True);
                    Assert.That(session.Bubbles.HeldSubtitle, Does.EndWith("?"));
                    Assert.That(session.Bubbles.LastRenderedBubbleCount, Is.Zero);
                    Assert.That(session.CameraDirector.CurrentShotIsClear, Is.True);
                    if (!Application.isBatchMode)
                    {
                        yield return Until(() => session.Bubbles.HasRenderedLayout && session.LastChoicePanelRect.height > 0f,
                            "subtitle and choice panel to render together at table " + table);
                        Assert.That(session.Bubbles.HasRenderedLayout, Is.True);
                        Assert.That(session.Bubbles.LastRenderedPanelRect.yMax, Is.LessThan(session.LastChoicePanelRect.yMin));
                    }
                    if (capture) yield return Capture("choice-" + table);

                    if (table == 0)
                    {
                        float stopped = session.SpeechClock;
                        using (GameTimeScaleRuntime.AcquirePause())
                        {
                            yield return null;
                            yield return null;
                            Assert.That(session.SpeechClock, Is.EqualTo(stopped));
                            Assert.That(session.Bubbles.RenderEnabled, Is.False);
                        }
                        yield return null;
                        yield return Press(keys.rightArrowKey);
                        Assert.That(session.SelectedChoice, Is.EqualTo(1));
                        yield return Press(keys.eKey);
                    }
                    else
                    {
                        yield return null;
                        Assert.That(session.Confirm(), Is.True);
                    }
                    yield return Until(() => session.Phase == DialoguePhase.Speaking &&
                        session.CurrentNode.Speaker == DialogueSpeaker.Hero, "hero answer");
                    if (capture && table == 0) yield return Capture("02-hero-subtitle");
                    yield return Until(() => session.IsChoosing && session.CurrentNode.Id == "round2c", "second question");
                    Assert.That(court.TableRound(table), Is.EqualTo(1));
                    if (table == 0)
                    {
                        yield return Press(keys.escapeKey);
                        yield return Until(() => !session.IsActive && player.InputEnabled, "cancel and release input");
                        Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
                        yield return Approach(0);
                        yield return null;
                        yield return Press(keys.eKey);
                        Assert.That(court.TableIsPending || session.IsActive, Is.True,
                            "A new E at the paid table must start a new request. " + session.LastEndReason);
                        yield return Until(() => session.IsChoosing, "resume without paying");
                        Assert.That(session.CurrentNode.Id, Is.EqualTo("round2c"));
                        Assert.That(DrinkCount(), Is.EqualTo(before - 2));
                    }
                    yield return null;
                    Assert.That(session.SelectChoice(2) && session.Confirm(), Is.True);
                    yield return Until(() => session.IsChoosing && session.CurrentNode.Id == "round3c", "third question");
                    Assert.That(session.Bubbles.HeldSubtitle, Does.EndWith("?"));
                    if (table == 2)
                    {
                        double departure = pair.Min(member => city.Pedestrians.Residents.Schedule(member.Id, GameSessionState.GameDayIndex,
                                GameSessionState.GameTimeOfDayMinutes).EndsAt);
                        double now = GameSessionState.GameDayIndex * 1440d + GameSessionState.GameTimeOfDayMinutes;
                        GameSessionState.AdvanceGameTime((float)((departure - now + .1d) /
                            GameTimeState.GameMinutesPerRealSecond));
                        yield return Until(() => !session.IsActive && player.InputEnabled, "visit ends while choosing");
                        Assert.That(GameSessionState.KioskDialogueRound(table, DefaultNpcPopulation.PedestrianId(pair[0].Id),
                            DefaultNpcPopulation.PedestrianId(pair[1].Id)), Is.EqualTo(2));
                        Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
                        Assert.That(follow.FixedPoseActive, Is.False);
                        break;
                    }
                    yield return null;
                    Assert.That(session.Confirm(), Is.True);
                    yield return Until(() => !session.IsActive && player.InputEnabled, "full dialogue exit");
                    Assert.That(court.TableRound(table), Is.EqualTo(3));
                    Assert.That(targets[table].CanInteract(player), Is.False);
                    Assert.That(follow.FixedPoseActive, Is.False);
                }
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.BeerCan), Is.EqualTo(1));
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.VodkaBottle), Is.Zero);
            }
            finally
            {
                if (station) station.ReleaseNpc(waitingCustomer);
                Time.captureDeltaTime = savedFrame;
                input.TearDown();
            }

            IEnumerator Approach(int table)
            {
                var target = targets[table];
                Vector3 ground = target.InteractionPosition - Vector3.up * .85f;
                city.Player.Motor.Teleport(ground + Vector3.up * PlayerFactory.GroundedRootOffset + Vector3.forward * .14f);
                player.transform.rotation = Quaternion.LookRotation(Vector3.back);
                Physics.SyncTransforms();
                follow.Snap();
                yield return Until(() => ReferenceEquals(player.ActiveInteractable, target), "table approach " + table);
            }

            static int DrinkCount() => GameSessionState.GetInventoryItemCount(InventoryItemId.BeerCan) +
                GameSessionState.GetInventoryItemCount(InventoryItemId.VodkaBottle);

            void AssertInShot(Vector3 point, string step)
            {
                Vector3 frame = city.Camera.WorldToViewportPoint(point);
                Assert.That(frame.z > 0f && frame.x is > 0f and < 1f && frame.y is > 0f and < 1f,
                    Is.True, step + " viewport=" + frame.ToString("F2"));
            }

            IEnumerator Press(KeyControl key)
            {
                input.Press(key, queueEventOnly: true);
                yield return null;
                input.Release(key, queueEventOnly: true);
                yield return null;
            }

            IEnumerator Until(Func<bool> condition, string label)
            {
                for (int frame = 0; frame < 330 && !condition(); frame++) yield return null;
                Assert.That(condition(), Is.True, label + " phase=" + session.Phase + " node=" + session.CurrentNode?.Id +
                    " end=" + session.LastEndReason + " pending=" + court.TableIsPending +
                    " request=" + court.RequestEndReason +
                    " table=" + string.Join(",", targets.Select(target => court.TableRound(target.Table))) +
                    " occupied=" + court.OccupiedPlaces + " player=" + player.transform.position.ToString("F2"));
            }

            IEnumerator Capture(string name)
            {
                yield return null;
                if (Application.isBatchMode)
                {
                    if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                        AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeKioskDialogue", name, 1280, 720);
                    yield break;
                }
                string path = Path.Combine(Application.dataPath, "../Captures/RidgeKioskDialogue", name + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                ScreenCapture.CaptureScreenshot(path);
                yield return null;
                yield return null;
            }
        }
    }
}
