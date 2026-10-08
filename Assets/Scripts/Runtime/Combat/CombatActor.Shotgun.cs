using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private Transform shotgunHinge, shotgunLeftMuzzle, shotgunRightMuzzle;
        private Quaternion shotgunHingeRest;
        private readonly Transform[] shotgunChambers = new Transform[2], shotgunShells = new Transform[2];
        private GameObject shotgunHandShell;
        private int shotgunReloadCue;
        private static readonly float[] ShotgunReloadCues = { .15f, .45f, .65f, 1.6f, 2.2f, 2.8f };
        internal CombatCasingPool ShotgunCasings { get; set; }
        internal int ShotgunVolleyResponseCount { get; private set; }
        internal Transform ShotgunMuzzle(int barrel) => barrel == 1 ? shotgunRightMuzzle : shotgunLeftMuzzle;
        private int ShotgunSpentMask => (Shotgun.ChamberSpent(0) ? 1 : 0) | (Shotgun.ChamberSpent(1) ? 2 : 0);

        private Vector3 ShotgunElbowHint(int root) => pistolArmBones[root].position +
            transform.right * (root == 0 ? .24f : -.24f) + transform.forward * .12f - transform.up * .22f;

        private void PlaceHeldFirearm()
        {
            if (IsShotgun) CombatShotgunAssetProvider.PlaceShotgun(Weapon, weaponGrip, handPose);
            else CombatPistolAssetProvider.PlacePistol(Weapon, weaponGrip, handPose);
        }

        private void InitializeShotgunMechanics()
        {
            shotgunHinge = CombatShotgunAssetProvider.FindAnchor(Weapon, "Hinge");
            shotgunHingeRest = shotgunHinge.localRotation;
            shotgunLeftMuzzle = CombatShotgunAssetProvider.FindAnchor(Weapon, "MuzzleLeft");
            shotgunRightMuzzle = CombatShotgunAssetProvider.FindAnchor(Weapon, "MuzzleRight");
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? "Left" : "Right";
                shotgunChambers[i] = CombatShotgunAssetProvider.FindAnchor(Weapon, "Chamber" + side);
                shotgunShells[i] = CombatShotgunAssetProvider.FindAnchor(Weapon, "Shell" + side);
            }
            shotgunHandShell = CombatShotgunAssetProvider.CreateShell(Weapon.transform);
            hero.RegisterAccessoryRenderers(shotgunHandShell.GetComponentsInChildren<Renderer>(true));
            UpdateShotgunMechanics();
        }

        private void UpdateShotgunMechanics()
        {
            if (Shotgun == null || shotgunHinge == null) return;
            // Semantic axes are corrected by the provider, while the imported unit scale stays on its root.
            shotgunHinge.localRotation = shotgunHingeRest * Quaternion.AngleAxis(48f * Shotgun.BreakOpen01, Vector3.right);
            for (int i = 0; i < 2; i++)
                shotgunShells[i].gameObject.SetActive(Shotgun.ChamberLoaded(i) || Shotgun.ChamberSpent(i));
            float time = Shotgun.ReloadElapsed;
            int barrel = time >= 1.8f && time < 2.2f ? 1 : time >= 1.05f && time < 1.6f ? 0 : -1;
            bool held = Shotgun.ReloadPending && barrel >= 0 && (Shotgun.ReloadChamberMask & (1 << barrel)) != 0;
            if (held) CombatShotgunAssetProvider.PlaceShellInHand(shotgunHandShell, hero.Registry.Anchors.LeftGrip, handPose);
            shotgunHandShell.SetActive(held);
        }

        private float ShotgunReloadAnimationSeconds
        {
            get
            {
                float time = Shotgun.ReloadProgress * pistolReload.length;
                // Reuse equal authored poses: pouch(.95) == pouch(1.8),
                // and open foreend(.65) == close foreend(2.4).
                // Only the empty left hand travels on the reversed return.
                if (Shotgun.ReloadChamberMask == 1 && time > 1.8f && time < 2.4f)
                    return Mathf.Lerp(.95f, .65f, (time - 1.8f) / .6f);
                if (Shotgun.ReloadChamberMask == 2 && time > .65f && time < 1.8f)
                    return Mathf.Lerp(.65f, .95f, Mathf.InverseLerp(1.5f, 1.8f, time));
                return time;
            }
        }

        private float ShotgunReloadGripWeight
        {
            get
            {
                float time = ShotgunReloadAnimationSeconds;
                if (time < .95f) return Mathf.Lerp(FirearmSupportGripWeight, .8f,
                    Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.65f, .95f, time)));
                return Mathf.Lerp(.8f, FirearmSupportGripWeight,
                    Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2.2f, 2.4f, time)));
            }
        }

        private void AdvanceShotgunReload(int spentBefore, bool wasReloading)
        {
            UpdateShotgunMechanics();
            if (!wasReloading) return;
            int ejected = spentBefore & ~ShotgunSpentMask;
            for (int i = 0; i < 2; i++)
                if ((ejected & (1 << i)) != 0) ShotgunCasings?.EjectShell(this, shotgunChambers[i], i);
            float through = Shotgun.IsReloading ? Shotgun.ReloadElapsed : Shotgun.Settings.ReloadSeconds;
            while (shotgunReloadCue < ShotgunReloadCues.Length && through + .000001f >= ShotgunReloadCues[shotgunReloadCue])
            {
                int cue = shotgunReloadCue++;
                if (cue == 3 && (Shotgun.ReloadChamberMask & 1) == 0 || cue == 4 && (Shotgun.ReloadChamberMask & 2) == 0) continue;
                RetroSfxId sound = cue == 5 ? RetroSfxId.ShotgunClose : cue < 2 ? RetroSfxId.ShotgunOpen :
                    cue == 2 ? RetroSfxId.PistolCasingEject : RetroSfxId.ShotgunShellInsert;
                RetroAudio.PlayAt(sound, cue >= 3 && cue < 5 ? handPose.CylinderCentre(true) : shotgunHinge.position);
            }
            if (!Shotgun.IsReloading) shotgunReloadCue = 0;
        }

        private static float ShotgunRecoilDegrees(float seconds)
        {
            if (seconds < .04f) return Mathf.SmoothStep(0f, 19f, seconds / .04f);
            if (seconds < .10f) return Mathf.SmoothStep(19f, 14f, (seconds - .04f) / .06f);
            if (seconds < .23f) return Mathf.SmoothStep(14f, 5f, (seconds - .10f) / .13f);
            if (seconds < .39f) return Mathf.SmoothStep(5f, -1.5f, (seconds - .23f) / .16f);
            return Mathf.SmoothStep(-1.5f, 0f, (seconds - .39f) / .16f);
        }

        // A batch resolves regional damage once, then publishes each physical entry wound.
        // Each later batch contributes only its new momentum/trauma; sound and hitstop start once per volley.
        internal void ReceiveShotgunVolley(CombatActor source, int sequence, List<CombatProjectilePool.PelletHit> hits,
            bool firstResponse)
        {
            if (hits.Count == 0) return;
            float damage = 0f, strongest = -1f;
            Vector3 impulse = Vector3.zero;
            int primary = 0, headIndex = -1;
            for (int i = 0; i < hits.Count; i++)
            {
                float regional = ProjectileDamageProfile.Shotgun.ResolveDamage(hits[i].Damage, hits[i].Hit.Location);
                damage += regional;
                Vector3 launch = (hits[i].Velocity.normalized + Vector3.up * .35f).normalized;
                impulse += launch * hits[i].Momentum;
                if (regional > strongest) { strongest = regional; primary = i; }
                if (headIndex < 0 && hits[i].Hit.Location.Region == MeleeBodyRegion.Head) headIndex = i;
            }
            MeleePhase before = State.Phase;
            float health = State.Health;
            bool wasDefeated = State.IsDefeated;
            MeleeHitResult result = wasDefeated ? MeleeHitResult.Hit :
                State.ReceiveProjectileHit(damage, default, .42f, ProjectileDamageProfile.Shotgun);
            if (result == MeleeHitResult.Ignored) return;
            bool terminal = !wasDefeated && State.IsDefeated;
            if (headIndex >= 0 && State.IsDefeated) primary = headIndex;
            var representative = hits[primary];
            var summary = new CombatImpact(source, this, sequence, representative.Hit.Point, representative.Hit.Normal,
                representative.Velocity.normalized, health, State.Health, result, representative.Hit.Location, 0f,
                representative.Hit.Part, representative.Hit.LocalPoint, representative.Velocity.magnitude,
                impulse, CombatImpactKind.Projectile, representative.Hit.LocalDirection, representative.Index);
            if (terminal) BeginProjectileDefeat(summary);
            if (firstResponse)
            {
                reaction = null; reactionClock = 0f; ShotgunVolleyResponseCount++;
                RetroAudio.PlayAt(RetroSfxId.SpadeBite, representative.Hit.Point);
                RetroAudio.PlayAt(RetroSfxId.StoneTamp, representative.Hit.Point, .7f);
            }
            float allocatedHealth = health;
            for (int n = 0; n < hits.Count; n++)
            {
                int i = (primary + n) % hits.Count;
                var pellet = hits[i];
                float nextHealth = Mathf.Max(0f, allocatedHealth - ProjectileDamageProfile.Shotgun.ResolveDamage(pellet.Damage, pellet.Hit.Location));
                var impact = new CombatImpact(source, this, sequence, pellet.Hit.Point, pellet.Hit.Normal,
                    pellet.Velocity.normalized, allocatedHealth, nextHealth, result, pellet.Hit.Location, 0f,
                    pellet.Hit.Part, pellet.Hit.LocalPoint, pellet.Velocity.magnitude,
                    n == 0 ? impulse : Vector3.zero, CombatImpactKind.Projectile, pellet.Hit.LocalDirection,
                    pellet.Index, n == 0 && firstResponse, true, pellet.Damage, pellet.HeadTrauma);
                PublishImpact(impact, before);
                allocatedHealth = nextHealth;
            }
            if (terminal) Ragdoll.BeginTerminalConvulsions();
            Present();
        }
    }
}
