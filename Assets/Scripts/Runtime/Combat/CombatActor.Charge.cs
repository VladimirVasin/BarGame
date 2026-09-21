using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        /// <summary>One swing side's clips; the rules' committed side selects the set.</summary>
        private struct SwingClips { public AnimationClip Attack, ReleaseLight, ReleaseHeavy, Charge, Recoil; }
        private readonly SwingClips[] swings = new SwingClips[2];
        private Vector3[] npcReleasePositions;
        private Quaternion[] npcReleaseRotations;
        private SwingClips Current => swings[(int)State.Swing];
        private AnimationClip ReleaseClip => State.AttackPower > 0f ? Current.ReleaseLight : Current.Attack;
        private bool IsRecoil(AnimationClip clip) => clip != null && (clip == swings[0].Recoil || clip == swings[1].Recoil);

        public bool RequestCharge()
        {
            if (roundEnded || !IsAvailable || !GameInput.CanRead(GameInputContext.Gameplay)) return false;
            if (!State.RequestCharge()) return false;
            if (State.IsCharging) { reaction = null; sweepValid = false; }
            Present();
            return true;
        }

        public bool ReleaseCharge()
        {
            if (roundEnded || !IsAvailable || !GameInput.CanRead(GameInputContext.Gameplay) || !State.ReleaseCharge()) return false;
            if (State.IsAttacking) { reaction = null; sweepValid = false; }
            Present();
            return true;
        }

        public bool CancelCharge()
        {
            if (!State.CancelCharge()) return false;
            sweepValid = false;
            if (isActiveAndEnabled && gameObject.activeInHierarchy) Present();
            return true;
        }

        private void LoadSwingClips(bool forNpc)
        {
            foreach (MeleeSwing swing in new[] { MeleeSwing.Forehand, MeleeSwing.Backhand })
            {
                CombatAssetProvider.SwingClipSet names = CombatAssetProvider.SwingClips(swing);
                swings[(int)swing] = new SwingClips
                {
                    Attack = CombatAssetProvider.LoadClip(names.Attack, forNpc),
                    ReleaseLight = CombatAssetProvider.LoadClip(names.ReleaseLight, forNpc),
                    ReleaseHeavy = CombatAssetProvider.LoadClip(names.ReleaseHeavy, forNpc),
                    Charge = CombatAssetProvider.LoadClip(names.Charge, forNpc),
                    Recoil = CombatAssetProvider.LoadClip(names.Recoil, forNpc)
                };
            }
        }

        private void InitializeNpcChargeBlend()
        {
            npcReleasePositions = new Vector3[npcPoseBones.Length];
            npcReleaseRotations = new Quaternion[npcPoseBones.Length];
        }

        private void SampleHeroRelease(float progress)
        {
            if (State.AttackPower > 0f)
                hero.SampleOwnedClipBlend(this, Current.ReleaseHeavy.name, State.AttackPower, progress);
            else hero.SampleOwnedClip(this, progress);
        }

        private void SampleHeroCharge()
        {
            // The side's charge(0) is its light release's first pose. Use the
            // same Playable blend as release: imported quaternion curves
            // and Unity's mixer do not interpolate intermediate weights alike.
            hero.SampleOwnedClipBlend(this, Current.ReleaseHeavy.name, State.Charge01, 0f);
        }

        private void SampleNpcRelease(float progress)
        {
            AnimationClip release = ReleaseClip, heavy = Current.ReleaseHeavy;
            release.SampleAnimation(npc.Animator.gameObject, progress * release.length);
            float power = State.AttackPower;
            if (power <= 0f) return;
            for (int i = 0; i < npcPoseBones.Length; i++)
            {
                npcReleasePositions[i] = npcPoseBones[i].localPosition;
                npcReleaseRotations[i] = npcPoseBones[i].localRotation;
            }
            heavy.SampleAnimation(npc.Animator.gameObject, progress * heavy.length);
            for (int i = 0; i < npcPoseBones.Length; i++)
            {
                Transform bone = npcPoseBones[i];
                bone.localPosition = Vector3.Lerp(npcReleasePositions[i], bone.localPosition, power);
                bone.localRotation = Quaternion.Slerp(npcReleaseRotations[i], bone.localRotation, power);
            }
        }
    }
}
