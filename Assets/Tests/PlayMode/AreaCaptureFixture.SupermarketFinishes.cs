using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Explicit("Supermarket finishes and the customer route.")]
        [PrebuildSetup(typeof(SupermarketRemodelAssetsSetup))]
        public IEnumerator SupermarketFinishes()
        {
            GameSessionState.BeginNewGame();
            SupermarketInteriorRoot shop = null;
            float oldStep = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = 1f / 30;
                yield return Capture(SceneIds.SupermarketInterior,
                    () => shop = Object.FindAnyObjectByType<SupermarketInteriorRoot>(),
                    () =>
                    {
                        shop.CameraFollow.enabled = false;
                        shop.Player.Motor.SetInputEnabled(false);
                        Transform room = shop.World.Root;
                        Shot View(string name, Vector3 eye, Vector3 look, float fov) =>
                            Shot.At("finishes-" + name, room.TransformPoint(eye), room.TransformPoint(look), fov);
                        return new[]
                        {
                            View("entrance", new Vector3(-.18f, 1.63f, -4.82f), new Vector3(-.4f, .82f, .1f), 65f),
                            View("cold", new Vector3(-1.95f, 1.55f, 2.22f), new Vector3(-.58f, 1.03f, 5.04f), 67f),
                            View("shelf", new Vector3(-1.52f, 1.47f, -2.86f), new Vector3(1.23f, 1.04f, -.64f), 65f),
                            View("checkout", new Vector3(4.46f, 1.62f, -3.86f), new Vector3(6.12f, 1.07f, -2.36f), 62f)
                        };
                    });
                yield return CheckSupermarketRoute(shop);
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            }
            finally
            {
                if (shop) { shop.CameraFollow.enabled = true; shop.Player.Motor.SetInputEnabled(true); }
                Time.captureDeltaTime = oldStep;
                GameSessionState.BeginNewGame();
            }
        }

        private static IEnumerator CheckSupermarketRoute(SupermarketInteriorRoot shop)
        {
            Transform room = shop.World.Root;
            Vector3 Point(float x, float z) => room.TransformPoint(new Vector3(x, 0f, z));
            var motor = shop.Player.Motor;
            motor.Teleport(Point(0, -4.5f));
            foreach (SupermarketShelfView shelf in shop.World.Shelves)
            {
                string fixture = shelf.Kind == SupermarketShelfKind.ColdShelf ? "Cold" :
                    shelf.Kind == SupermarketShelfKind.DryGoods ? "Dry" : "Pantry";
                Assert.That(shop.World.AssetRegistry.TryGetPart("Shelf " + fixture + " Tiers", out var tiers), Is.True);
                // Probe the actual imported deck. The normal gameplay collider encloses all tiers.
                var probe = tiers.Renderer.gameObject.AddComponent<MeshCollider>();
                try
                {
                    probe.sharedMesh = tiers.Renderer.GetComponent<MeshFilter>().sharedMesh;
                    Physics.SyncTransforms();
                    foreach (var product in shelf.Products)
                    {
                        Assert.That(product.TryGetWorldBounds(out Bounds bounds), Is.True);
                        AssertOnMarketDeck(probe, bounds, product.SourceId);
                        Transform stock = shelf.transform.Find(SupermarketStockDisplay.RootPrefix + product.ItemId);
                        Assert.That(stock, Is.Not.Null);
                        foreach (Transform copy in stock)
                        {
                            var meshes = copy.GetComponentsInChildren<MeshRenderer>();
                            Assert.That(meshes.Length, Is.Positive, copy.name);
                            Bounds copyBounds = meshes[0].bounds;
                            foreach (var renderer in meshes) copyBounds.Encapsulate(renderer.bounds);
                            AssertOnMarketDeck(probe, copyBounds, product.SourceId + "/" + copy.name);
                        }
                        var label = product.OriginalRoot.GetComponentInChildren<TMPro.TextMeshPro>();
                        Assert.That(label, Is.Not.Null, product.SourceId);
                        Assert.That(label.text, Is.EqualTo(SupermarketProductCatalog.GetOffer(product.ItemId).Price.ToString()));
                        Vector3 aisle = room.TransformDirection(shelf.Plan.FacingDirection);
                        Assert.That(Vector3.Dot(-label.transform.forward, aisle), Is.GreaterThan(.99f));
                        label.ForceMeshUpdate();
                        Assert.That(label.textInfo.characterCount, Is.EqualTo(label.text.Length));
                        for (int i = 0; i < label.textInfo.characterCount; i++)
                            Assert.That(label.textInfo.characterInfo[i].isVisible, Is.True);
                        Assert.That(label.renderedWidth, Is.LessThan(label.rectTransform.rect.width));
                    }
                }
                finally { Object.DestroyImmediate(probe); }
            }
            yield return WalkChurchGardenLeg(motor, Point(-1.25f, -3.4f), "entrance to centre aisle");
            yield return WalkChurchGardenLeg(motor, Point(-1.25f, 3.35f), "length of centre aisle");
            yield return WalkChurchGardenLeg(motor, Point(.40f, 3.75f), "cold display");
            var cold = shop.World.Shelves.First(s => s.Kind == SupermarketShelfKind.ColdShelf);
            motor.SetInputEnabled(true);
            shop.CameraFollow.enabled = true;
            int cash = GameSessionState.CashBalance;
            const InventoryItemId item = InventoryItemId.ChickenEgg;
            int count = GameSessionState.GetInventoryItemCount(item);
            Assert.That(shop.ShelfShop.Open(cold, shop.Player.Interactor), Is.True);
            int index = shop.ShelfShop.Products.ToList().FindIndex(p => p.ItemId == item);
            Assert.That(shop.ShelfShop.Select(index), Is.True);
            yield return null;
            yield return null;
            Assert.That(shop.ShelfShop.ConfirmSelection(), Is.True);
            var receipt = WorldItemFoundScreen.For(shop.Player.Interactor);
            float deadline = Time.realtimeSinceStartup + 12f;
            while (!receipt.IsShowing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(receipt.Dismiss(), Is.True);
            while (shop.ShelfShop.IsReceiptOpen && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(shop.ShelfShop.IsReceiptOpen, Is.False);
            shop.ShelfShop.Close();
            Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash - SupermarketProductCatalog.GetOffer(item).Price));
            Assert.That(GameSessionState.GetInventoryItemCount(item), Is.EqualTo(count + 1));
            Assert.That(motor.InputEnabled && shop.Player.Interactor.InputEnabled, Is.True);
            yield return WalkChurchGardenLeg(motor, Point(3.15f, 3.4f), "around the pantry end");
            yield return WalkChurchGardenLeg(motor, Point(4.8f, -2.25f), "checkout");
            yield return WalkChurchGardenLeg(motor, Point(2.85f, -3.5f), "front cross aisle");
            yield return WalkChurchGardenLeg(motor, Point(0, -4.5f), "return to entrance");
        }

        private static void AssertOnMarketDeck(MeshCollider deck, Bounds bounds, string name)
        {
            var bottom = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            Assert.That(deck.Raycast(new Ray(bottom + Vector3.up * .06f, Vector3.down),
                out RaycastHit hit, .12f), Is.True, name);
            Assert.That(hit.point.y, Is.EqualTo(bottom.y).Within(.012f), name);
        }
    }
}
