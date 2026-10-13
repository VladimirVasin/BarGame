namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private int journalReloadRequest;
        private int journalCarryActionRequest;
        private int journalCarryQueuedRequest;

        private static string JournalWeaponName(CombatWeaponId id) => id switch
        {
            CombatWeaponId.Crowbar => "Crowbar",
            CombatWeaponId.Pistol => "Pistol",
            CombatWeaponId.Shotgun => "Shotgun",
            _ => "Unknown"
        };

        private void JournalReload(string name, int request, string reason, bool resuming)
        {
            if (name == "pistol_reload_started" || name == "shotgun_reload_started") journalReloadRequest = request;
            JournalEvent(name, request: request == 0 ? journalReloadRequest : request,
                f0: GameLog.Field("weapon", JournalWeaponName(WeaponId)),
                f1: GameLog.Field("rounds", Firearm?.Rounds ?? 0),
                f2: GameLog.Field("reserve", Firearm?.ReserveRounds ?? 0),
                f3: GameLog.Field("reason", reason), f4: GameLog.Field("resuming", resuming),
                f5: GameLog.Field("reload_progress", Firearm?.ReloadProgress ?? 0f),
                f6: GameLog.Field("chamber_mask", (Firearm as ShotgunState)?.ReloadChamberMask ?? 0),
                f7: GameLog.Field("load_mask", (Firearm as ShotgunState)?.ReloadLoadMask ?? 0));
        }

        internal void JournalAmmunitionPickup(CombatWeaponId id, int before, int accepted)
        {
            IFirearmState firearm = CarriedFirearm(id);
            if (firearm == null) return;
            JournalEvent(accepted > 0 ? "ammo_picked_up" : "ammo_pickup_rejected",
                f0: GameLog.Field("weapon", JournalWeaponName(id)), f1: GameLog.Field("accepted", accepted),
                f2: GameLog.Field("reserve_before", before), f3: GameLog.Field("reserve_after", firearm.ReserveRounds),
                f4: GameLog.Field("rounds", firearm.Rounds), f5: GameLog.Field("capacity", firearm.ReserveCapacity),
                f6: GameLog.Field("reason", accepted > 0 ? "accepted" : "reserve_full"));
        }

        private void JournalWeaponCarry(string phase, CarriedWeaponRecord record, string reason = "accepted", int stage = 0)
        {
            JournalEvent("weapon_carry", request: phase == "request" ? JournalRequestId : journalCarryActionRequest,
                f0: GameLog.Field("phase", phase), f1: GameLog.Field("weapon", JournalWeaponName(record?.Id ?? WeaponId)),
                f2: GameLog.Field("reason", reason), f3: GameLog.Field("drawing", carryDraw),
                f4: GameLog.Field("location", (int)(record?.Location ?? CombatWeaponCarryLocation.Stowed)),
                f5: GameLog.Field("progress", WeaponCarryActionProgress), f6: GameLog.Field("stage", stage),
                f7: GameLog.Field("reserve", record?.Firearm?.ReserveRounds ?? 0));
        }
    }
}
