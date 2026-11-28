using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal static class InventoryIconLibrary
    {
        private static readonly Dictionary<InventoryItemId, Texture2D> Icons =
            new Dictionary<InventoryItemId, Texture2D>();
        private static Texture2D heroPortrait;
        private static readonly Texture2D[] corruptionPortraits = new Texture2D[7];
        private static readonly bool[] corruptionPortraitResolved = new bool[7];

        internal const string HeroPortraitResourcePath =
            "Player/Corruption/Portrait1";

        internal static readonly Rect HeroPortraitUv =
            new Rect(0f, 0f, 1f, 1f);

        public static Texture2D GetIcon(InventoryItemId itemId)
        {
            if (Icons.TryGetValue(itemId, out Texture2D texture) &&
                texture != null)
            {
                return texture;
            }

            texture = CreateItemIcon(itemId);
            Icons[itemId] = texture;
            return texture;
        }

        public static Texture2D GetHeroPortrait()
        {
            if (heroPortrait == null)
            {
                heroPortrait = Resources.Load<Texture2D>(
                    HeroPortraitResourcePath);
                if (heroPortrait == null)
                {
                    throw new System.InvalidOperationException(
                        "Missing dedicated 3D player portrait at Resources/" +
                        HeroPortraitResourcePath + ".");
                }
            }

            int level = GameSessionState.HeroCorruptionLevel;
            int index = level - HeroCorruptionRules.FirstLevel;
            if (level >= HeroCorruptionRules.FirstLevel)
            {
                if (!corruptionPortraitResolved[index])
                {
                    corruptionPortraitResolved[index] = true;
                    corruptionPortraits[index] = Resources.Load<Texture2D>("Player/Corruption/Portrait" + level);
                }
                if (corruptionPortraits[index] != null) return corruptionPortraits[index];
            }
            return heroPortrait;
        }

        private static Texture2D CreateItemIcon(InventoryItemId itemId)
        {
            var painter = new PixelPainter(32, 32);
            Color32 ink = new Color32(27, 25, 29, 255);
            Color32 pale = new Color32(224, 216, 190, 255);
            Color32 metal = new Color32(126, 142, 138, 255);
            Color32 amber = new Color32(190, 121, 48, 255);
            switch (itemId)
            {
                case InventoryItemId.ChestFlashlight:
                    painter.FillRect(10, 5, 12, 19, metal);
                    painter.OutlineRect(9, 4, 14, 21, ink);
                    painter.FillRect(12, 7, 2, 14, pale);
                    painter.FillRect(8, 19, 16, 7, metal);
                    painter.OutlineRect(7, 18, 18, 9, ink);
                    painter.Ellipse(16, 22, 5, 3, pale);
                    painter.EllipseOutline(16, 22, 5, 3, ink);
                    break;
                case InventoryItemId.HomeBills:
                    painter.FillRect(2, 7, 28, 18, pale);
                    painter.OutlineRect(2, 7, 28, 18, ink);
                    for (int offset = 0; offset <= 12; offset++)
                    {
                        painter.Set(3 + offset, 23 - offset / 2, metal);
                        painter.Set(28 - offset, 23 - offset / 2, metal);
                    }
                    break;
                case InventoryItemId.LodgeGroupPhotograph:
                    // The same small framed group silhouette as the authored prop.
                    painter.FillRect(3, 6, 26, 20, new Color32(102, 73, 48, 255));
                    painter.OutlineRect(2, 5, 28, 22, ink);
                    painter.FillRect(5, 8, 22, 16, pale);
                    painter.FillRect(6, 9, 20, 7, new Color32(91, 105, 99, 255));
                    for (int person = 0; person < 5; person++)
                    {
                        int x = 7 + person * 4;
                        painter.FillRect(x, 11, 3, person == 2 ? 8 : 5, ink);
                        painter.FillRect(x, person == 2 ? 19 : 16, 3, 3, amber);
                    }
                    painter.FillRect(14, 20, 2, 4, amber);
                    painter.FillRect(14, 24, 2, 1, metal);
                    break;
                case InventoryItemId.ChildShoeLast:
                    painter.Ellipse(15,16,23,9,amber);
                    painter.FillRect(8,14,8,8,amber);
                    painter.FillRect(11,19,4,2,metal);
                    painter.FillRect(18,13,2,5,pale);
                    break;
                case InventoryItemId.SilverBuckleShoes:
                    for (int shoe=0;shoe<2;shoe++)
                    {
                        int x=10+shoe*11;
                        painter.Ellipse(x,15,9,19,metal);
                        painter.Ellipse(x,19,5,6,ink);
                        painter.OutlineRect(x-3,14,6,4,pale);
                    }
                    break;
                case InventoryItemId.MourningLocket:
                    painter.Ellipse(10,15,13,17,amber);
                    painter.Ellipse(22,15,13,17,pale);
                    painter.Ellipse(22,15,9,12,ink);
                    painter.EllipseOutline(22,15,6,9,amber);
                    break;
                case InventoryItemId.BracedBoot:
                    painter.FillRect(8,11,11,15,metal);
                    painter.Ellipse(17,10,21,8,ink);
                    painter.FillRect(10,8,2,18,pale);
                    painter.FillRect(9,23,11,2,amber);
                    break;
                case InventoryItemId.ClampMirror:
                    painter.FillRect(8,12,17,14,metal);
                    painter.OutlineRect(8,12,17,14,pale);
                    painter.FillRect(15,6,3,6,amber);
                    painter.FillRect(10,5,11,3,metal);
                    break;
                case InventoryItemId.BedsideBell:
                    painter.Ellipse(13,15,19,12,amber);
                    painter.FillRect(4,9,20,3,ink);
                    painter.FillRect(12,21,15,2,pale);
                    break;
                case InventoryItemId.TailorShears:
                    painter.Ring(10,10,5,2,amber);
                    painter.Ring(21,10,5,2,metal);
                    for (int i=0;i<14;i++)
                    {
                        painter.FillRect(13+i/3,13+i,2,2,pale);
                        painter.FillRect(18-i/3,13+i,2,2,metal);
                    }
                    break;
                case InventoryItemId.NotchedGauge:
                    painter.FillRect(3,12,26,8,amber);
                    for (int i=0;i<7;i++) painter.FillRect(6+i*3,17+i%2,1,3,ink);
                    break;
                case InventoryItemId.CordedWeddingRings:
                    painter.EllipseOutline(16,20,19,13,metal);
                    painter.Ring(12,12,6,2,amber);
                    painter.Ring(21,12,5,2,pale);
                    break;
                case InventoryItemId.FoldingScrewdriver:
                    painter.FillRect(4, 13, 16, 7, amber);
                    painter.OutlineRect(4, 13, 16, 7, ink);
                    painter.FillRect(18, 15, 10, 3, metal);
                    break;
                case InventoryItemId.Caliper:
                    painter.FillRect(4, 12, 24, 4, metal);
                    painter.FillRect(5, 15, 3, 8, pale);
                    painter.FillRect(14, 15, 3, 7, pale);
                    painter.OutlineRect(12, 10, 7, 7, ink);
                    break;
                case InventoryItemId.PocketRadio:
                    painter.FillRect(6, 7, 20, 18, metal);
                    painter.OutlineRect(6, 7, 20, 18, ink);
                    painter.FillRect(18, 10, 5, 12, pale);
                    for (int i = 10; i < 23; i += 3) painter.FillRect(8, i, 8, 1, ink);
                    break;
                case InventoryItemId.MechanicalWatch:
                    painter.FillRect(13, 5, 6, 23, amber);
                    painter.Ellipse(16, 17, 13, 13, pale);
                    painter.EllipseOutline(16, 17, 13, 13, ink);
                    painter.FillRect(15, 13, 2, 6, ink);
                    painter.FillRect(15, 17, 5, 1, ink);
                    break;
                case InventoryItemId.SilverRing:
                    painter.Ring(16, 16, 9, 4, metal);
                    painter.FillRect(10, 22, 5, 1, pale);
                    break;
                case InventoryItemId.FirewoodLog:
                    Color32 bark = new Color32(111, 76, 49, 255);
                    Color32 grain = new Color32(183, 148, 95, 255);
                    painter.FillRect(7, 11, 19, 11, bark);
                    painter.OutlineRect(7, 10, 20, 13, ink);
                    painter.FillRect(11, 13, 14, 1, ink);
                    painter.FillRect(12, 19, 12, 1, amber);
                    painter.Ellipse(8, 16, 5, 7, grain);
                    painter.EllipseOutline(8, 16, 5, 7, ink);
                    painter.EllipseOutline(8, 16, 2, 4, bark);
                    break;
                case InventoryItemId.Scarf:
                    Color32 cloth = new Color32(224, 191, 52, 255);
                    Color32 weave = new Color32(249, 223, 113, 255);
                    painter.FillRect(7, 15, 18, 9, cloth);
                    painter.OutlineRect(6, 14, 20, 11, ink);
                    painter.FillRect(10, 7, 6, 10, cloth);
                    painter.FillRect(19, 6, 5, 10, cloth);
                    painter.FillRect(8, 20, 16, 1, weave);
                    painter.FillRect(11, 7, 1, 6, weave);
                    painter.FillRect(20, 6, 1, 7, weave);
                    break;
                case InventoryItemId.ApartmentKeys:
                    painter.Ring(9, 18, 6, 3, metal);
                    painter.FillRect(13, 16, 13, 4, metal);
                    painter.FillRect(22, 12, 3, 5, metal);
                    painter.FillRect(26, 14, 3, 4, metal);
                    painter.OutlineRect(2, 7, 27, 18, ink);
                    break;
                case InventoryItemId.Lighter:
                    // The closed metal flip-top shared with the physical stove prop.
                    painter.FillRect(8, 5, 17, 23, metal);
                    painter.OutlineRect(7, 4, 19, 25, ink);
                    painter.FillRect(9, 7, 2, 18, pale);
                    painter.FillRect(8, 20, 17, 1, ink);
                    painter.FillRect(7, 19, 3, 3, metal);
                    break;
                case InventoryItemId.EmptyBeerCan:
                case InventoryItemId.BeerCanLastSip:
                case InventoryItemId.BeerCan:
                    painter.FillRect(10, 6, 13, 21, metal);
                    painter.OutlineRect(9, 5, 15, 23, ink);
                    painter.FillRect(10, 11, 13, 10, amber);
                    painter.FillRect(11, 25, 11, 2, pale);
                    painter.OutlineRect(14, 25, 5, 2, ink);
                    break;
                case InventoryItemId.EmptyHawthornBottle:
                case InventoryItemId.HawthornTincture:
                    painter.FillRect(10, 7, 12, 15, new Color32(99, 63, 39, 255));
                    painter.OutlineRect(9, 6, 14, 17, ink);
                    painter.FillRect(12, 23, 8, 4, metal);
                    painter.OutlineRect(11, 22, 10, 6, ink);
                    painter.FillRect(10, 11, 12, 8, pale);
                    painter.FillRect(12, 16, 8, 1, ink);
                    painter.FillRect(12, 13, 6, 1, ink);
                    break;
                case InventoryItemId.EmptyWineBottle:
                case InventoryItemId.WineBottleLastSip:
                case InventoryItemId.WineBottle:
                case InventoryItemId.EmptyVodkaBottle:
                case InventoryItemId.VodkaBottle:
                    painter.FillRect(13, 22, 6, 5, metal);
                    painter.FillRect(11, 9, 10, 14, pale);
                    painter.OutlineRect(10, 8, 12, 16, ink);
                    painter.OutlineRect(12, 21, 8, 7, ink);
                    painter.FillRect(12, 12, 8, 5, amber);
                    break;
                case InventoryItemId.ChickenEgg:
                    painter.Ellipse(16, 16, 8, 11, pale);
                    painter.EllipseOutline(16, 16, 9, 12, ink);
                    painter.Set(12, 20, new Color32(246, 238, 211, 255));
                    break;
                case InventoryItemId.OpenStewCan:
                    painter.FillRect(8, 8, 16, 16, metal);
                    painter.OutlineRect(7, 7, 18, 18, ink);
                    painter.FillRect(9, 12, 14, 7, amber);
                    painter.FillRect(9, 23, 14, 2, pale);
                    painter.FillRect(9, 7, 14, 2, pale);
                    break;
                case InventoryItemId.ClosedStewCan:
                    painter.FillRect(8, 8, 16, 16, metal);
                    painter.OutlineRect(7, 7, 18, 18, ink);
                    painter.FillRect(9, 12, 14, 7, amber);
                    painter.FillRect(9, 23, 14, 2, pale);
                    painter.FillRect(9, 7, 14, 2, pale);
                    painter.OutlineRect(12, 20, 8, 3, ink);
                    break;
                case InventoryItemId.InstantNoodles:
                    painter.FillRect(5, 8, 22, 16, amber);
                    painter.OutlineRect(4, 7, 24, 18, ink);
                    painter.FillRect(8, 11, 16, 9, pale);
                    painter.FillRect(10, 14, 12, 2, ink);
                    painter.FillRect(6, 8, 2, 16, metal);
                    painter.FillRect(24, 8, 2, 16, metal);
                    break;
                case InventoryItemId.PotatoPirozhok:
                    painter.Ellipse(16, 17, 12, 7, amber);
                    painter.EllipseOutline(16, 17, 12, 7, ink);
                    painter.FillRect(6, 9, 13, 6, pale);
                    painter.FillRect(5, 15, 8, 3, pale);
                    painter.FillRect(10, 11, 3, 1, metal);
                    painter.FillRect(18, 19, 5, 1, ink);
                    break;
                case InventoryItemId.BreadHeel:
                case InventoryItemId.DayOldLoaf:
                    painter.FillRect(6, 9, 20, 14, amber);
                    painter.FillRect(8, 22, 16, 3, pale);
                    painter.OutlineRect(5, 8, 22, 16, ink);
                    painter.FillRect(11, 21, 2, 4, ink);
                    painter.FillRect(16, 21, 2, 4, ink);
                    painter.FillRect(21, 21, 2, 4, ink);
                    painter.FillRect(19, 9, 4, 14, metal);
                    break;
            }

            return painter.CreateTexture("Inventory Icon " + itemId);
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetResources()
        {
            foreach (Texture2D texture in Icons.Values)
            {
                DestroyTexture(texture);
            }

            Icons.Clear();
            heroPortrait = null;
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(texture);
            }
            else
            {
                Object.DestroyImmediate(texture);
            }
        }

    }
}
