using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private AnimationClip charge, releaseLight, releaseHeavy;
        private Vector3[] npcReleasePositions;
        private Quaternion[] npcReleaseRotations;
        private AnimationClip ReleaseClip => State.AttackPower > 0f ? releaseLight : attack;

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

        private void InitializeNpcChargeBlend()
        {
            npcReleasePositions = new Vector3[npcPoseBones.Length];
            npcReleaseRotations = new Quaternion[npcPoseBones.Length];
        }

        private void SampleHeroRelease(float progress)
        {
            if (State.AttackPower > 0f)
                hero.SampleOwnedClipBlend(this, releaseHeavy.name, State.AttackPower, progress);
            else hero.SampleOwnedClip(this, progress);
        }

        private void SampleHeroCharge()
        {
            // CombatCharge(0) is the light release's first pose. Use the
            // same Playable blend as release: imported quaternion curves
            // and Unity's mixer do not interpolate intermediate weights alike.
            hero.SampleOwnedClipBlend(this, releaseHeavy.name, State.Charge01, 0f);
        }

        private void SampleNpcRelease(float progress)
        {
            ReleaseClip.SampleAnimation(npc.Animator.gameObject, progress * ReleaseClip.length);
            float power = State.AttackPower;
            if (power <= 0f) return;
            for (int i = 0; i < npcPoseBones.Length; i++)
            {
                npcReleasePositions[i] = npcPoseBones[i].localPosition;
                npcReleaseRotations[i] = npcPoseBones[i].localRotation;
            }
            releaseHeavy.SampleAnimation(npc.Animator.gameObject, progress * releaseHeavy.length);
            for (int i = 0; i < npcPoseBones.Length; i++)
            {
                Transform bone = npcPoseBones[i];
                bone.localPosition = Vector3.Lerp(npcReleasePositions[i], bone.localPosition, power);
                bone.localRotation = Quaternion.Slerp(npcReleaseRotations[i], bone.localRotation, power);
            }
        }
    }
}
