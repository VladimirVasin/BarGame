using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Timeout(300000)]
        [Explicit("Walk the three apartments, leave and take every find, then sell each at the actual counter.")]
        public IEnumerator RavineFamiliesFromRoomsToPawnshop()
        {
            var devices = new InputTestFixture();
            devices.Setup();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            float delta = Time.captureDeltaTime;
            bool focused = GameCursor.ApplicationFocused;
            try
            {
                GameCursor.SetApplicationFocused(true);
                GameSessionState.BeginNewGame("Ravine households");
                GameSessionState.TryStartGameTimeAt(12*60);
                yield return SceneManager.LoadSceneAsync(SceneIds.City,LoadSceneMode.Single);
                var city = Object.FindAnyObjectByType<CityGameRoot>();
                yield return Until(() => city.IsInitialized,"City composition",90f);
                var site = RidgeRavinePlan.ForLayout(city.Layout);
                var room = site.Families;
                Vector3 At(float x,float floor,float z) => site.Origin + new Vector3(x,floor*site.Storey,z);
                var interactor = city.Player.Interactor;
                var screen = WorldItemFoundScreen.For(interactor);
                var pickups = Object.FindObjectsByType<WorldItemPickup>().Where(p => p.Plan.SourceId.StartsWith("city.ravine.")).ToDictionary(p=>p.Plan.ItemId);
                Assert.That(pickups.Count,Is.EqualTo(9));
                Time.captureDeltaTime = .05f;
                var follow = city.Camera.GetComponent<PlayerCameraFollow>();
                follow.enabled = false;
                foreach (var r in city.Player.GameObject.GetComponentsInChildren<Renderer>()) r.enabled = false;
                var mirror = pickups[InventoryItemId.ClampMirror].GetComponent<RavineBridgeMirror>();
                Vector3 eye = site.Origin + room.pillow;
                SetWestInfillCamera(city,eye,mirror.GlassPoint);
                yield return Until(() => mirror.BridgeView && mirror.BridgeView.IsCreated(),"bridge reflected from the pillow");
                var bridge = CityHillsideQuarterPlan.ForLayout(city.Layout).Bridges[1];
                Vector3 reflected = Vector3.Reflect((mirror.GlassPoint-eye).normalized,mirror.Normal);
                float toRail = (bridge.DeckBounds.yMax-.09f-mirror.GlassPoint.z)/reflected.z;
                Vector3 target = mirror.GlassPoint + reflected*toRail;
                Assert.That(target.x,Is.InRange(bridge.DeckBounds.xMin,bridge.DeckBounds.xMax));
                Assert.That(target.y,Is.InRange(bridge.DeckCenterWorld.y+.4f,bridge.DeckCenterWorld.y+1.45f));
                CaptureCurrentCamera(city.Camera,"RavineFamilies","pillow",1100,680);
                // The ray must clear the sash, sill and near rail all the way across the deck.
                bool hit = Physics.Raycast(mirror.GlassPoint+reflected*.035f,reflected,out var surface,
                    toRail+.55f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
                bool bridgeInMirror = hit && surface.transform.IsChildOf(GameObject.Find("Upper ravine bridge").transform);
                string sightLine = "Mirror-to-bridge line from " + mirror.GlassPoint + " toward " + target +
                    (hit ? " hit " + surface.transform.name + " at " + surface.point : " stayed open");
                void Frame(string title,Vector3 from,Vector3 to)
                {
                    SetWestInfillCamera(city,from,to);
                    CaptureCurrentCamera(city.Camera,"RavineFamilies",title,1100,680);
                }
                Frame("clamp", mirror.GlassPoint + (eye-mirror.GlassPoint).normalized*.85f - Vector3.forward*.23f,
                    mirror.GlassPoint-Vector3.up*.12f);
                Frame("shoemaker", At(.8f,0,1.64f)+Vector3.up*1.42f,At(-1,0,3.83f)+Vector3.up*.46f);
                Frame("bedroom", At(1.38f,1,1.39f)+Vector3.up*1.44f,At(-.65f,1,4.04f)+Vector3.up*.62f);
                Frame("folded-cot", At(.33f,1,-1.44f)+Vector3.up*1.43f,At(-.24f,1,-4.08f)+Vector3.up*.48f);
                Frame("tailors", At(1.52f,2,-1.99f)+Vector3.up*1.48f,At(1.26f,2,-4.0f)+Vector3.up*1.15f);
                Debug.Log(sightLine);
                city.Player.Motor.Teleport(At(4.56f,0,3.42f)+Vector3.up*PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms();
                for (int floor=0;floor<3;floor++)
                {
                    yield return WalkParkLeg(city,At(4.56f,floor,2.14f),"Hall landing");
                    if (floor>0) Assert.That(city.Player.Motor.transform.position.y,Is.GreaterThan(site.Origin.y+floor*site.Storey-.07f));
                    Vector3 north=At(.28f,floor,2.14f), south=At(.28f,floor,-1.96f);
                    yield return WalkParkLeg(city,north,"Front apartment door");
                    Vector3 hub=north;
                    foreach (var dock in room.docks.Where(d=>Mathf.RoundToInt(d.approach.y/site.Storey)==floor))
                    {
                        if (dock.approach.z < 0 && hub==north) { yield return WalkParkLeg(city,south,"Through the partition"); hub=south; }
                        var id=(InventoryItemId)Enum.Parse(typeof(InventoryItemId),dock.item);
                        var pickup=pickups[id];
                        var stand=site.Origin+dock.approach;
                        yield return WalkParkLeg(city,stand,"Approach "+id);
                        yield return Until(()=>ReferenceEquals(interactor.ActiveInteractable,pickup) &&
                            !interactor.PromptView.IsFeedbackVisible,id+" selectable at "+stand);
                        Assert.That(LocalizationService.Get(pickup.PromptKey),Does.StartWith("E"));
                        yield return Press(keyboard.eKey);
                        yield return Until(()=>screen.IsShowing,id+" inspection");
                        Assert.That(screen.ActiveItemId,Is.EqualTo(id));
                        Assert.That(screen.DescriptionLabel,Is.Not.EqualTo(screen.DescriptionKey));
                        yield return Press(keyboard.escapeKey);
                        yield return Until(()=>!screen.IsPresenting && interactor.InputEnabled,id+" left in the room");
                        Assert.That(GameSessionState.IsWorldItemCollected(pickup.Plan.SourceId),Is.False);
                        string source=pickup.Plan.SourceId;
                        yield return Until(()=>ReferenceEquals(interactor.ActiveInteractable,pickup),id+" selectable again");
                        yield return Press(keyboard.eKey);
                        yield return Until(()=>screen.IsShowing,id+" second inspection");
                        yield return Press(keyboard.eKey);
                        yield return Until(()=>!pickup && interactor.InputEnabled,id+" collected");
                        Assert.That(GameSessionState.GetInventoryItemCount(id),Is.EqualTo(1));
                        Assert.That(GameSessionState.IsWorldItemCollected(source),Is.True);
                        yield return WalkParkLeg(city,hub,"Return from "+id);
                    }
                    yield return WalkParkLeg(city,north,"Back to front room");
                    yield return WalkParkLeg(city,At(4.56f,floor,2.14f),"Foot of stairs");
                    if (floor==2) break;
                    yield return WalkParkLeg(city,At(4.56f,floor+.5f,-2.21f),"Lower flight");
                    yield return WalkParkLeg(city,At(5.92f,floor+.5f,-2.21f),"Half landing");
                    yield return WalkParkLeg(city,At(5.92f,floor+1,2.14f),"Upper flight");
                }

                yield return SceneManager.LoadSceneAsync(SceneIds.PawnshopInterior,LoadSceneMode.Single);
                var shop = Object.FindAnyObjectByType<PawnshopInteriorRoot>();
                yield return Until(()=>shop && shop.IsInitialized,"clerk");
                shop.Player.Motor.Teleport(shop.Layout.TradePosition+Vector3.up*PlayerFactory.GroundedRootOffset);
                screen=WorldItemFoundScreen.For(shop.Player.Interactor);
                Physics.SyncTransforms();
                int balance=GameSessionState.CashBalance;
                var offers=PawnshopFindCatalog.Offers.Where(o=>pickups.ContainsKey(o.Item)).ToList();
                Assert.That(offers.Count,Is.EqualTo(9));
                // Buy the bottom entry on the second page first. The rest move up as each sale removes its row.
                while (offers.Count>0)
                {
                    bool last=offers.Count==9;
                    var offer=last ? offers[offers.Count-1] : offers[0];
                    yield return Until(()=>ReferenceEquals(shop.Player.Interactor.ActiveInteractable,shop.Trade) &&
                        !shop.InteractionPrompt.IsFeedbackVisible,"counter available");
                    yield return Press(keyboard.eKey);
                    Assert.That(shop.Trade.SelectSell(),Is.True);
                    if (last) { yield return Press(keyboard.upArrowKey); yield return Press(keyboard.upArrowKey); }
                    yield return Press(keyboard.eKey);
                    yield return Until(()=>screen.IsShowing,"quote "+offer.Item);
                    Assert.That(screen.ActiveItemId,Is.EqualTo(offer.Item));
                    Assert.That(screen.ActionLabel,Does.Contain("$"+offer.Price));
                    yield return Press(keyboard.eKey);
                    balance+=offer.Price;
                    yield return Until(()=>GameSessionState.CashBalance==balance && !shop.Trade.IsOpen,"tray handoff "+offer.Item);
                    Assert.That(GameSessionState.GetInventoryItemCount(offer.Item),Is.Zero);
                    Assert.That(GameSessionState.IsWorldItemCollected("city.ravine."+offer.Item),Is.True);
                    offers.Remove(offer);
                }
                Assert.That(bridgeInMirror,Is.True,sightLine);

                IEnumerator Press(ButtonControl button)
                {
                    keyboard.MakeCurrent();
                    devices.Press(button,queueEventOnly:true);
                    yield return null;
                    devices.Release(button,queueEventOnly:true);
                    yield return null;
                }
                IEnumerator Until(Func<bool> ready,string label,float seconds=9f)
                {
                    float end=Time.realtimeSinceStartup+seconds;
                    while (!ready() && Time.realtimeSinceStartup<end) yield return null;
                    Assert.That(ready(),Is.True,label);
                }
            }
            finally
            {
                devices.TearDown();
                GameCursor.SetApplicationFocused(focused);
                Time.captureDeltaTime=delta;
                GameSessionState.BeginNewGame("Ravine households complete");
            }
        }
    }
}
