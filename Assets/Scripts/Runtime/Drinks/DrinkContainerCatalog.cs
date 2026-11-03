using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Finite authored container profiles shared by the shelf, inventory and the one hand owner.</summary>
    public sealed class DrinkContainerProfile
    {
        internal DrinkContainerProfile(InventoryItemId item, int volume, int sips, float height, float radius,
            float gripY, Vector3 mouth, Vector4[] innerProfile, bool can)
        {
            ItemId = item; FullMl = volume; SipCount = sips; Height = height; Radius = radius;
            GripHeight = gripY; Mouth = mouth; InnerProfile = innerProfile; IsCan = can;
        }
        public InventoryItemId ItemId { get; }
        public int FullMl { get; }
        public int SipCount { get; }
        public float Height { get; }
        public float Radius { get; }
        public float GripHeight { get; }
        public Vector3 Mouth { get; }
        public Vector4[] InnerProfile { get; }
        public bool IsCan { get; }
        public string GripAnchorName => ItemId == InventoryItemId.HawthornTincture ? "HawthornGrip" : IsCan ? "BeerCanGrip" : ItemId == InventoryItemId.WineBottle ? "WineBottleGrip" : "VodkaGrip";
        public string MouthAnchorName => ItemId == InventoryItemId.HawthornTincture ? "HawthornMouth" : IsCan ? "BeerCanMouth" : ItemId == InventoryItemId.WineBottle ? "WineBottleMouth" : "VodkaMouth";
        public float FillFraction => ItemId == InventoryItemId.VodkaBottle ? .82f : .96f;
        public float InnerBottom => InnerProfile[0].x;
        public float InnerTop => InnerProfile[InnerProfile.Length - 1].x;
        public float InnerRadius => ItemId == InventoryItemId.HawthornTincture ? .016f : IsCan ? .0355f : ItemId == InventoryItemId.WineBottle ? .035f : VodkaBottleDimensions.InnerBoundsRadius;
        public float SurfaceScale => ItemId == InventoryItemId.HawthornTincture ? 2f : IsCan ? 3.2f : ItemId == InventoryItemId.WineBottle ? 4f : VodkaBottleDimensions.LiquidSurfaceScale;
        public float LiquidCentre => (InnerBottom + InnerTop) * .5f;
        public NpcHandGripKind GripKind => IsCan ? NpcHandGripKind.WineBowl : NpcHandGripKind.Bottle;
    }

    public static class DrinkContainerCatalog
    {
        private static readonly DrinkContainerProfile Hawthorn = new DrinkContainerProfile(
            InventoryItemId.HawthornTincture, 50, 1, .108f, .0185f, .052f, Vector3.up * .102f, new[]
            { new Vector4(.005f,.014f,0f,0f), new Vector4(.009f,.016f,0f,0f), new Vector4(.065f,.016f,0f,0f),
              new Vector4(.079f,.0065f,0f,0f), new Vector4(.092f,.0065f,0f,0f), new Vector4(.098f,.0065f,0f,0f), new Vector4(.101f,.0065f,0f,0f) }, false);
        private static readonly DrinkContainerProfile Vodka = new DrinkContainerProfile(
            InventoryItemId.VodkaBottle, 500, 15, VodkaBottleDimensions.Height, VodkaBottleDimensions.BodyRadius,
            VodkaBottleDimensions.GripHeight, Vector3.up * VodkaBottleDimensions.MouthHeight, VodkaBottleDimensions.InnerProfile, false);
        private static readonly DrinkContainerProfile Beer = new DrinkContainerProfile(
            InventoryItemId.BeerCan, 500, 10, .153f, .037f, .080f, new Vector3(0f, .149f, -.012f), new[]
            { new Vector4(.006f,.030f,0f,0f), new Vector4(.012f,.0355f,0f,0f), new Vector4(.035f,.0355f,0f,0f), new Vector4(.075f,.0355f,0f,0f),
              new Vector4(.120f,.0355f,0f,0f), new Vector4(.140f,.033f,0f,0f), new Vector4(.148f,.030f,0f,0f) }, true);
        private static readonly DrinkContainerProfile Wine = new DrinkContainerProfile(
            InventoryItemId.WineBottle, 750, 15, .305f, .0385f, .232f, Vector3.up * .294f, new[]
            { new Vector4(.009f,.031f,0f,0f), new Vector4(.016f,.035f,0f,0f), new Vector4(.184f,.035f,0f,0f), new Vector4(.216f,.0125f,0f,0f),
              new Vector4(.249f,.0125f,0f,0f), new Vector4(.284f,.0125f,0f,0f), new Vector4(.292f,.0125f,0f,0f) }, false);
        public static bool IsDrink(InventoryItemId item) => item is InventoryItemId.VodkaBottle or InventoryItemId.BeerCan or InventoryItemId.WineBottle or InventoryItemId.HawthornTincture;
        public static DrinkContainerProfile Get(InventoryItemId item) => item switch
        {
            InventoryItemId.VodkaBottle => Vodka, InventoryItemId.BeerCan => Beer, InventoryItemId.WineBottle => Wine,
            InventoryItemId.HawthornTincture => Hawthorn,
            _ => throw new ArgumentOutOfRangeException(nameof(item))
        };
    }
}
