using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private Vector3[] craneFrenzyPositions, craneFrenzyScales;
        private Quaternion[] craneFrenzyRestRotations, craneFrenzyInverseReferences;
        private bool craneFrenzyResumeTravelling, craneFrenzyResumeChangingFloors;
        private CombatCranePhase craneFrenzyResumePhase;
        private AudioClip craneFrenzySound, craneFrenzyFirstSound, craneFrenzySecondSound;
        public bool IsCraneFrenzied => IsCrane && IsZombieFrenzied;
        public float CraneFrenzySecondsElapsed => IsCrane ? ZombieFrenzySecondsElapsed : 0f;
        public int CraneFrenzyPlayCount { get; private set; }

        private void InitializeCraneFrenzy()
        {
            int count = npcAnimationBaseBones.Length;
            craneFrenzyPositions = new Vector3[count]; craneFrenzyScales = new Vector3[count];
            craneFrenzyRestRotations = new Quaternion[count]; craneFrenzyInverseReferences = new Quaternion[count];
            craneFrenzyFirstSound = CombatZombieAssetProvider.LoadAudio("ZombieFrenzy");
            craneFrenzySecondSound = CombatZombieAssetProvider.LoadAudio("ZombieFrenzy2");
            SampleNpcClip(ready, 0f);
            for (int i = 0; i < count; i++)
            {
                Transform bone = npcAnimationBaseBones[i];
                craneFrenzyPositions[i] = bone.localPosition; craneFrenzyScales[i] = bone.localScale;
                craneFrenzyRestRotations[i] = bone.localRotation;
            }
            SampleNpcClip(zombieFrenzy, 0f);
            for (int i = 0; i < count; i++) craneFrenzyInverseReferences[i] = Quaternion.Inverse(npcAnimationBaseBones[i].localRotation);
            RestoreCraneFrenzyRestPose();
            SampleNpcClip(ready, 0f);
        }

        private void BeginCraneFrenzy(int sequence)
        {
            craneFrenzyResumeTravelling = craneTravelling;
            craneFrenzyResumeChangingFloors = craneChangingFloors;
            craneFrenzyResumePhase = CranePhase;
            craneTravelling = craneChangingFloors = false;
            CraneMotionAxis = 0; craneMoveSpeed = 0f;
            craneHurtHold = craneHurtClock = 0f;
            craneContactValid = collectSweep = sweepValid = collectShove = collectKick = false;
            reaction = null; locomotionVelocity = Vector3.zero;
            State.CancelAction(); craneStrikeStopped = true;
            StopCraneVoices();
            SetCranePhase(CombatCranePhase.Frenzied);
            if (CraneHookPhase is CombatCraneHookPhase.Outbound or CombatCraneHookPhase.Holding or CombatCraneHookPhase.Returning)
                BeginCraneHookReturn();
            craneFrenzySound = (sequence & 1) == 0 ? craneFrenzyFirstSound : craneFrenzySecondSound;
            craneBodyVoice.clip = craneFrenzySound; craneBodyVoice.volume = .7f; craneBodyVoice.Play();
            if (presentationFrozen || GameTimeScaleRuntime.IsPaused) craneBodyVoice.Pause();
            CraneFrenzyPlayCount++;
        }

        private void AdvanceCraneFrenzy(float seconds)
        {
            AdvanceCraneHook(seconds);
            // Returning metal cannot deal another hit, even if its old strike
            // still had a pending contact window when the victim fell.
            craneContactValid = collectSweep = sweepValid = false;
            AdvanceCraneDynamics(seconds, Vector3.zero);
            Vector3 previous = transform.position;
            if (!MoveCraneRoot(CraneLoadPosition, transform.rotation, craneCommand))
            {
                craneSwayVelocity *= -.15f;
                craneSway = Vector3.ProjectOnPlane(transform.position - craneCommand, Vector3.up);
            }
            UpdateCraneMachinePose();
            locomotionVelocity = (transform.position - previous) / Mathf.Max(.0001f, seconds);
            AdvanceZombieFrenzy(seconds);
        }

        private void PresentCraneFrenzy()
        {
            SampleNpcClip(zombieFrenzy, ZombieFrenzySecondsElapsed);
            float weight = Mathf.SmoothStep(0f, 1f, ZombieFrenzySecondsElapsed / .12f) *
                Mathf.SmoothStep(0f, 1f, (ZombieFrenzyDurationSeconds - ZombieFrenzySecondsElapsed) / .45f);
            for (int i = 0; i < npcAnimationBaseBones.Length; i++)
            {
                Transform bone = npcAnimationBaseBones[i];
                Quaternion delta = craneFrenzyInverseReferences[i] * bone.localRotation;
                // Reuse the authored impulse score, never the porter's shortened
                // anatomy, grounded root sink or planted knee/boot translations.
                bone.SetLocalPositionAndRotation(craneFrenzyPositions[i],
                    craneFrenzyRestRotations[i] * Quaternion.Slerp(Quaternion.identity, delta, weight));
                bone.localScale = craneFrenzyScales[i];
            }
        }

        private void RestoreCraneFrenzyRestPose()
        {
            for (int i = 0; i < npcAnimationBaseBones.Length; i++)
            {
                npcAnimationBaseBones[i].SetLocalPositionAndRotation(craneFrenzyPositions[i], craneFrenzyRestRotations[i]);
                npcAnimationBaseBones[i].localScale = craneFrenzyScales[i];
            }
        }

        private void EndCraneFrenzy()
        {
            if (craneBodyVoice != null)
            {
                if (craneBodyVoice.clip == craneFrenzySound) craneBodyVoice.Stop();
                craneBodyVoice.volume = .2f;
            }
            if (CranePhase != CombatCranePhase.Frenzied) return;
            bool resume = isActiveAndEnabled && !roundEnded && !State.IsDefeated && !State.IsStunned &&
                !IsKnockedDown && !IsRagdollActive && BodyDamage.CanStand;
            craneTravelling = resume && craneFrenzyResumeTravelling;
            craneChangingFloors = craneTravelling && craneFrenzyResumeChangingFloors;
            craneFrenzyResumeTravelling = craneFrenzyResumeChangingFloors = false;
            if (craneTravelling)
            {
                craneStarting = true; craneMotorClock = craneBrakeClock = 0f;
                SetCranePhase(craneFrenzyResumePhase);
            }
            else SetCranePhase(CraneHookPhase == CombatCraneHookPhase.Stowed ? CombatCranePhase.Waiting : CombatCranePhase.Recovering);
        }
    }
}
