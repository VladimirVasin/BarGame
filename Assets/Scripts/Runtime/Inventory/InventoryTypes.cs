using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public enum InventoryItemId
    {
        None = 0,
        ApartmentKeys = 1,
        Lighter = 2,
        VodkaBottle = 3,
        ChickenEgg = 4,
        OpenStewCan = 5,
        ClosedStewCan = 6,
        InstantNoodles = 7,
        DayOldLoaf = 8,
        Scarf = 9,
        FirewoodLog = 10,
        LodgeGroupPhotograph = 11,
        CombatCrowbar = 12,
        CombatPistol = 13,
        CombatShotgun = 14,
        CombatPistolAmmo = 15,
        CombatShotgunAmmo = 16,
        RottenFishTailCan = 17,
        RottenFishBellyCan = 18,
        RottenFishHeadCan = 19,
        WarehouseGateKey = 20,
        ChestFlashlight = 21,
        BeerCan = 22,
        WineBottle = 23,
        HawthornTincture = 24,
        PotatoPirozhok = 25,
        EmptyWineBottle = 26,
        EmptyBeerCan = 27,
        WineBottleLastSip = 28,
        BeerCanLastSip = 29,
        BreadHeel = 30,
        EmptyVodkaBottle = 31,
        EmptyHawthornBottle = 32,
        FoldingScrewdriver = 33,
        Caliper = 34,
        PocketRadio = 35,
        MechanicalWatch = 36,
        SilverRing = 37,
        ChildShoeLast = 38,
        SilverBuckleShoes = 39,
        MourningLocket = 40,
        BracedBoot = 41,
        ClampMirror = 42,
        BedsideBell = 43,
        TailorShears = 44,
        NotchedGauge = 45,
        CordedWeddingRings = 46,
        HomeBills = 47
    }

    public enum InventoryItemCategory
    {
        KeyItem = 0,
        Tool = 1,
        Consumable = 2,
        Clothing = 3
    }

    public readonly struct InventoryItemDefinition
    {
        internal InventoryItemDefinition(
            InventoryItemId id,
            InventoryItemCategory category,
            string nameLocalizationKey,
            string descriptionLocalizationKey,
            int maximumStack)
        {
            Id = id;
            Category = category;
            NameLocalizationKey = nameLocalizationKey;
            DescriptionLocalizationKey = descriptionLocalizationKey;
            MaximumStack = maximumStack;
        }

        public InventoryItemId Id { get; }
        public InventoryItemCategory Category { get; }
        public string NameLocalizationKey { get; }
        public string DescriptionLocalizationKey { get; }
        public int MaximumStack { get; }
        public bool IsEquippable => Category == InventoryItemCategory.Clothing ||
            Id is InventoryItemId.ChestFlashlight or InventoryItemId.CombatCrowbar or InventoryItemId.CombatPistol ||
            DrinkContainerCatalog.IsDrink(Id) && Id != InventoryItemId.HawthornTincture;
    }

    public readonly struct InventoryItemStack
    {
        internal InventoryItemStack(InventoryItemId itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }

        public InventoryItemId ItemId { get; }
        public int Count { get; }
    }

    public static class InventoryItemCatalog
    {
        internal static string Describe(InventoryItemDefinition definition) =>
            definition.Id == InventoryItemId.HomeBills
                ? HomeBills.Description
                : LocalizationService.Get(definition.DescriptionLocalizationKey);

        private static readonly InventoryItemDefinition[] Definitions =
        {
            new InventoryItemDefinition(InventoryItemId.HomeBills, InventoryItemCategory.KeyItem,
                "inventory.item.home_bills.name", "inventory.item.home_bills.description", 1),
            new InventoryItemDefinition(InventoryItemId.ChildShoeLast, InventoryItemCategory.Tool,
                "inventory.item.child_shoe_last.name", "inventory.item.child_shoe_last.description", 1),
            new InventoryItemDefinition(InventoryItemId.SilverBuckleShoes, InventoryItemCategory.Tool,
                "inventory.item.silver_buckle_shoes.name", "inventory.item.silver_buckle_shoes.description", 1),
            new InventoryItemDefinition(InventoryItemId.MourningLocket, InventoryItemCategory.Tool,
                "inventory.item.mourning_locket.name", "inventory.item.mourning_locket.description", 1),
            new InventoryItemDefinition(InventoryItemId.BracedBoot, InventoryItemCategory.Tool,
                "inventory.item.braced_boot.name", "inventory.item.braced_boot.description", 1),
            new InventoryItemDefinition(InventoryItemId.ClampMirror, InventoryItemCategory.Tool,
                "inventory.item.clamp_mirror.name", "inventory.item.clamp_mirror.description", 1),
            new InventoryItemDefinition(InventoryItemId.BedsideBell, InventoryItemCategory.Tool,
                "inventory.item.bedside_bell.name", "inventory.item.bedside_bell.description", 1),
            new InventoryItemDefinition(InventoryItemId.TailorShears, InventoryItemCategory.Tool,
                "inventory.item.tailor_shears.name", "inventory.item.tailor_shears.description", 1),
            new InventoryItemDefinition(InventoryItemId.NotchedGauge, InventoryItemCategory.Tool,
                "inventory.item.notched_gauge.name", "inventory.item.notched_gauge.description", 1),
            new InventoryItemDefinition(InventoryItemId.CordedWeddingRings, InventoryItemCategory.Tool,
                "inventory.item.corded_wedding_rings.name", "inventory.item.corded_wedding_rings.description", 1),
            new InventoryItemDefinition(InventoryItemId.FoldingScrewdriver, InventoryItemCategory.Tool,
                "inventory.item.folding_screwdriver.name", "inventory.item.folding_screwdriver.description", 9),
            new InventoryItemDefinition(InventoryItemId.Caliper, InventoryItemCategory.Tool,
                "inventory.item.caliper.name", "inventory.item.caliper.description", 9),
            new InventoryItemDefinition(InventoryItemId.PocketRadio, InventoryItemCategory.Tool,
                "inventory.item.pocket_radio.name", "inventory.item.pocket_radio.description", 9),
            new InventoryItemDefinition(InventoryItemId.MechanicalWatch, InventoryItemCategory.Tool,
                "inventory.item.mechanical_watch.name", "inventory.item.mechanical_watch.description", 9),
            new InventoryItemDefinition(InventoryItemId.SilverRing, InventoryItemCategory.Tool,
                "inventory.item.silver_ring.name", "inventory.item.silver_ring.description", 9),
            new InventoryItemDefinition(InventoryItemId.EmptyVodkaBottle, InventoryItemCategory.Tool,
                "inventory.item.empty_vodka_bottle.name", "inventory.item.empty_vodka_bottle.description", 9),
            new InventoryItemDefinition(InventoryItemId.EmptyHawthornBottle, InventoryItemCategory.Tool,
                "inventory.item.empty_hawthorn_bottle.name", "inventory.item.empty_hawthorn_bottle.description", 9),
            new InventoryItemDefinition(InventoryItemId.EmptyWineBottle, InventoryItemCategory.Tool,
                "inventory.item.empty_wine_bottle.name", "inventory.item.empty_wine_bottle.description", 9),
            new InventoryItemDefinition(InventoryItemId.EmptyBeerCan, InventoryItemCategory.Tool,
                "inventory.item.empty_beer_can.name", "inventory.item.empty_beer_can.description", 9),
            new InventoryItemDefinition(InventoryItemId.WineBottleLastSip, InventoryItemCategory.Consumable,
                "inventory.item.wine_bottle_last_sip.name", "inventory.item.wine_bottle_last_sip.description", 9),
            new InventoryItemDefinition(InventoryItemId.BeerCanLastSip, InventoryItemCategory.Consumable,
                "inventory.item.beer_can_last_sip.name", "inventory.item.beer_can_last_sip.description", 9),
            new InventoryItemDefinition(InventoryItemId.BreadHeel, InventoryItemCategory.Consumable,
                "inventory.item.bread_heel.name", "inventory.item.bread_heel.description", 9),
            new InventoryItemDefinition(InventoryItemId.PotatoPirozhok, InventoryItemCategory.Consumable,
                "inventory.item.potato_pirozhok.name", "inventory.item.potato_pirozhok.description", 9),
            new InventoryItemDefinition(InventoryItemId.HawthornTincture, InventoryItemCategory.Consumable,
                "inventory.item.hawthorn.name", "inventory.item.hawthorn.description", 9),
            new InventoryItemDefinition(InventoryItemId.BeerCan, InventoryItemCategory.Consumable,
                "inventory.item.beer_can.name", "inventory.item.beer_can.description", 9),
            new InventoryItemDefinition(InventoryItemId.WineBottle, InventoryItemCategory.Consumable,
                "inventory.item.wine_bottle.name", "inventory.item.wine_bottle.description", 9),
            new InventoryItemDefinition(
                InventoryItemId.ApartmentKeys,
                InventoryItemCategory.KeyItem,
                "inventory.item.apartment_keys.name",
                "inventory.item.apartment_keys.description",
                1),
            new InventoryItemDefinition(
                InventoryItemId.Lighter,
                InventoryItemCategory.Tool,
                "inventory.item.lighter.name",
                "inventory.item.lighter.description",
                1),
            new InventoryItemDefinition(
                InventoryItemId.VodkaBottle,
                InventoryItemCategory.Consumable,
                "inventory.item.vodka_bottle.name",
                "inventory.item.vodka_bottle.description",
                9),
            new InventoryItemDefinition(
                InventoryItemId.ChickenEgg,
                InventoryItemCategory.Consumable,
                "inventory.item.chicken_egg.name",
                "inventory.item.chicken_egg.description",
                9),
            new InventoryItemDefinition(
                InventoryItemId.OpenStewCan,
                InventoryItemCategory.Consumable,
                "inventory.item.open_stew_can.name",
                "inventory.item.open_stew_can.description",
                9),
            new InventoryItemDefinition(
                InventoryItemId.ClosedStewCan,
                InventoryItemCategory.Consumable,
                "inventory.item.closed_stew_can.name",
                "inventory.item.closed_stew_can.description",
                9),
            new InventoryItemDefinition(
                InventoryItemId.InstantNoodles,
                InventoryItemCategory.Consumable,
                "inventory.item.instant_noodles.name",
                "inventory.item.instant_noodles.description",
                9),
            new InventoryItemDefinition(
                InventoryItemId.DayOldLoaf,
                InventoryItemCategory.Consumable,
                "inventory.item.day_old_loaf.name",
                "inventory.item.day_old_loaf.description",
                9),
            new InventoryItemDefinition(
                InventoryItemId.Scarf,
                InventoryItemCategory.Clothing,
                "inventory.item.scarf.name",
                "inventory.item.scarf.description",
                1),
            new InventoryItemDefinition(
                InventoryItemId.FirewoodLog,
                InventoryItemCategory.Tool,
                "inventory.item.firewood_log.name",
                "inventory.item.firewood_log.description",
                1),
            new InventoryItemDefinition(
                InventoryItemId.CombatCrowbar,
                InventoryItemCategory.Tool,
                "inventory.item.combat_crowbar.name",
                "inventory.item.combat_crowbar.description",
                1),
            new InventoryItemDefinition(
                InventoryItemId.CombatPistol,
                InventoryItemCategory.Tool,
                "inventory.item.combat_pistol.name",
                "inventory.item.combat_pistol.description",
                1),
            new InventoryItemDefinition(
                InventoryItemId.CombatShotgun,
                InventoryItemCategory.Tool,
                "inventory.item.combat_shotgun.name",
                "inventory.item.combat_shotgun.description",
                1),
            new InventoryItemDefinition(
                InventoryItemId.CombatPistolAmmo,
                InventoryItemCategory.Consumable,
                "inventory.item.combat_pistol_ammo.name",
                "inventory.item.combat_pistol_ammo.description",
                1),
            new InventoryItemDefinition(
                InventoryItemId.CombatShotgunAmmo,
                InventoryItemCategory.Consumable,
                "inventory.item.combat_shotgun_ammo.name",
                "inventory.item.combat_shotgun_ammo.description",
                1),
            new InventoryItemDefinition(
                InventoryItemId.LodgeGroupPhotograph,
                InventoryItemCategory.KeyItem,
                "inventory.item.lodge_group_photograph.name",
                "inventory.item.lodge_group_photograph.description",
                1),
            new InventoryItemDefinition(InventoryItemId.RottenFishTailCan, InventoryItemCategory.KeyItem,
                "inventory.item.rotten_fish_tail_can.name", "inventory.item.rotten_fish_tail_can.description", 1),
            new InventoryItemDefinition(InventoryItemId.RottenFishBellyCan, InventoryItemCategory.KeyItem,
                "inventory.item.rotten_fish_belly_can.name", "inventory.item.rotten_fish_belly_can.description", 1),
            new InventoryItemDefinition(InventoryItemId.RottenFishHeadCan, InventoryItemCategory.KeyItem,
                "inventory.item.rotten_fish_head_can.name", "inventory.item.rotten_fish_head_can.description", 1),
            new InventoryItemDefinition(InventoryItemId.WarehouseGateKey, InventoryItemCategory.KeyItem,
                "inventory.item.warehouse_gate_key.name", "inventory.item.warehouse_gate_key.description", 1),
            new InventoryItemDefinition(InventoryItemId.ChestFlashlight, InventoryItemCategory.Tool,
                "inventory.item.chest_flashlight.name", "inventory.item.chest_flashlight.description", 1)
        };

        private static readonly IReadOnlyList<InventoryItemDefinition>
            DefinitionsView = Array.AsReadOnly(Definitions);

        public static IReadOnlyList<InventoryItemDefinition> All =>
            DefinitionsView;

        public static bool TryGet(
            InventoryItemId id,
            out InventoryItemDefinition definition)
        {
            for (int index = 0; index < Definitions.Length; index++)
            {
                if (Definitions[index].Id == id)
                {
                    definition = Definitions[index];
                    return true;
                }
            }

            definition = default;
            return false;
        }

        public static InventoryItemDefinition Get(InventoryItemId id)
        {
            if (TryGet(id, out InventoryItemDefinition definition))
            {
                return definition;
            }

            throw new ArgumentOutOfRangeException(
                nameof(id),
                id,
                "The item is not present in the inventory catalog.");
        }
    }
}
