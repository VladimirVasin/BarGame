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
        private bool[] npcReleaseUpperBody;
        private SwingClips Current => swings[(int)State.Swing];
        private AnimationClip ReleaseClip => State.AttackPower > 0f ? Current.ReleaseLight : Current.Attack;
        private bool IsRecoil(AnimationClip clip) => clip != null && (clip == swings[0].Recoil || clip == swings[1].Recoil);

        public bool RequestCharge()
        {
            if (roundEnded || !IsAvailable || !HasTwoHandSupport || !GameInput.CanRead(GameInputContext.Gameplay)) return false;
            if (!State.RequestCharge()) return false;
            if (State.IsCharging) { reaction = null; sweepValid = false; }
            Present();
            return true;
        }

        public bool ReleaseCharge()
        {
            if (roundEnded || !IsAvailable || !HasTwoHandSupport || !GameInput.CanRead(GameInputContext.Gameplay) || !State.ReleaseCharge()) return false;
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
            npcReleaseUpperBody = new bool[npcPoseBones.Length];
            Transform spine = null;
            foreach (Transform bone in npcPoseBones)
                if (bone.name == "spine") { spine = bone; break; }
            for (int i = 0; i < npcPoseBones.Length; i++)
                npcReleaseUpperBody[i] = spine != null && npcPoseBones[i].IsChildOf(spine);
        }

        private void SampleHeroRelease(float progress)
        {
            hero.SampleOwnedClipUpperTime(this, Current.Attack.name, progress,
                CombatAssetProvider.ReleaseSourceSeconds(progress * Current.Attack.length, State.AttackPower) / Current.Attack.length);
        }

        private void SampleHeroCharge()
        {
            hero.SampleOwnedClipUpperTime(this, Current.Attack.name, 0f,
                CombatAssetProvider.ReleaseSourceSeconds(0f, State.Charge01) / Current.Attack.length);
        }

        private void SampleNpcRelease(float progress)
        {
            SampleNpcReleasePose(progress * Current.Attack.length, State.AttackPower);
        }

        private void SampleNpcReleasePose(float seconds, float power)
        {
            AnimationClip clip = Current.Attack;
            clip.SampleAnimation(npc.Animator.gameObject, seconds);
            for (int i = 0; i < npcPoseBones.Length; i++)
            {
                if (npcReleaseUpperBody[i]) continue;
                npcReleasePositions[i] = npcPoseBones[i].localPosition;
                npcReleaseRotations[i] = npcPoseBones[i].localRotation;
            }
            clip.SampleAnimation(npc.Animator.gameObject, CombatAssetProvider.ReleaseSourceSeconds(seconds, power));
            for (int i = 0; i < npcPoseBones.Length; i++)
            {
                if (npcReleaseUpperBody[i]) continue;
                npcPoseBones[i].localPosition = npcReleasePositions[i];
                npcPoseBones[i].localRotation = npcReleaseRotations[i];
            }
        }
    }
}
