using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private readonly Vector3[] ammoPositions = new Vector3[CombatAmmoSpawner.Capacity];
        private static readonly Vector3[] AmmoPlacementTargets =
        {
            new Vector3(-6f, 0f, -4f), new Vector3(6f, 0f, -4f),
            new Vector3(-6f, WarehouseArenaPlan.UpperHeight, 7f),
            new Vector3(6f, WarehouseArenaPlan.UpperHeight, 7f)
        };
        internal CombatAmmoSpawner AmmoSpawns { get; private set; }

        private void OnEnable() => AmmoSpawns?.SetInteractionEnabled(
            Hero != null && !Hero.State.IsDefeated && pistolApplicationFocused);

        private void InitializeAmmoPickups()
        {
            ResolveAmmoPositions();
            AmmoSpawns = new CombatAmmoSpawner(transform, Hero, ammoPositions);
        }

        private void ResetAmmoPickups()
        {
            ResolveAmmoPositions();
            AmmoSpawns.Reset(ammoPositions);
            AmmoSpawns.SetInteractionEnabled(isActiveAndEnabled && !Hero.State.IsDefeated && pistolApplicationFocused);
        }

        private void AdvanceAmmoPickups(float seconds)
        {
            AmmoSpawns.SetInteractionEnabled(isActiveAndEnabled && !Hero.State.IsDefeated && pistolApplicationFocused);
            AmmoSpawns.Advance(seconds);
        }

        private void ResolveAmmoPositions()
        {
            if (WarehousePlan == null)
            {
                float floor = heroSpawn.y - PlayerFactory.GroundedRootOffset;
                ammoPositions[0] = new Vector3(-4f, floor, -3f);
                ammoPositions[1] = new Vector3(4f, floor, -3f);
                ammoPositions[2] = new Vector3(-4f, floor, 4f);
                ammoPositions[3] = new Vector3(4f, floor, 4f);
                return;
            }
            var area = (WarehouseArenaArea)arenaBounds;
            // Select on immutable validated routes only at initialization/reset/layout changes.
            // Two lower and two upper positions spread both ammunition types across the level.
            for (int slot = 0; slot < ammoPositions.Length; slot++)
            {
                Vector3 target = AmmoPlacementTargets[slot];
                float best = float.PositiveInfinity;
                bool found = false;
                foreach (Vector3 node in WarehousePlan.NavigationNodes)
                {
                    if (Mathf.Abs(node.y - target.y) > .1f || !area.TryGetHeight(node, out float floor, .5f) ||
                        Mathf.Abs(node.y - floor) > .05f) continue;
                    bool separated = true;
                    for (int previous = 0; previous < slot; previous++)
                        if ((node - ammoPositions[previous]).sqrMagnitude < 2.25f) { separated = false; break; }
                    float distance = (node - target).sqrMagnitude;
                    if (!separated || distance >= best) continue;
                    best = distance;
                    ammoPositions[slot] = new Vector3(node.x, floor, node.z);
                    found = true;
                }
                if (!found) throw new InvalidOperationException("The validated arena has no walkable ammunition position.");
            }
        }
    }
}
