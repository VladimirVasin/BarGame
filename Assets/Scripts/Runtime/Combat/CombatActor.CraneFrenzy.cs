using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private Vector3[] craneFrenzyPositions, craneFrenzyScales;
        private Quaternion[] craneFrenzyRestRotations, craneFrenzyInverseReferences;
        private float[] craneFrenzyLimbWeights;
        private AudioClip craneFrenzySound, craneFrenzyFirstSound, craneFrenzySecondSound;
        public bool IsCraneFrenzied => IsCrane && CranePhase == CombatCranePhase.Frenzied;
        public float CraneFrenzySecondsElapsed { get; private set; }
        public int CraneFrenzyPlayCount { get; private set; }

        private void InitializeCraneFrenzy()
        {
            int count = npcAnimationBaseBones.Length;
            craneFrenzyPositions = new Vector3[count]; craneFrenzyScales = new Vector3[count];
            craneFrenzyRestRotations = new Quaternion[count]; craneFrenzyInverseReferences = new Quaternion[count];
            craneFrenzyLimbWeights = new float[count];
            craneFrenzyFirstSound = CombatZombieAssetProvider.LoadAudio("ZombieFrenzy");
            craneFrenzySecondSound = CombatZombieAssetProvider.LoadAudio("ZombieFrenzy2");
            SampleNpcClip(ready, 0f);
            for (int i = 0; i < count; i++)
            {
                Transform bone = npcAnimationBaseBones[i];
                craneFrenzyPositions[i] = bone.localPosition; craneFrenzyScales[i] = bone.localScale;
                craneFrenzyRestRotations[i] = bone.localRotation;
                craneFrenzyLimbWeights[i] = bone.name.StartsWith("upper_arm.", System.StringComparison.Ordinal) ? 1f :
                    bone.name.StartsWith("thigh.", System.StringComparison.Ordinal) ? .75f : 0f;
            }
            SampleNpcClip(zombieFrenzy, 0f);
            for (int i = 0; i < count; i++) craneFrenzyInverseReferences[i] = Quaternion.Inverse(npcAnimationBaseBones[i].localRotation);
            RestoreCraneFrenzyRestPose();
            SampleNpcClip(ready, 0f);
        }

        private void BeginCraneFatalSpasm()
        {
            craneTravelling = craneChangingFloors = false;
            CraneMotionAxis = 0; craneMoveSpeed = 0f;
            craneHurtHold = craneHurtClock = 0f;
            craneContactValid = collectSweep = sweepValid = collectShove = collectKick = false;
            reaction = null; locomotionVelocity = Vector3.zero;
            State.CancelAction(); craneStrikeStopped = true;
            StopCraneVoices();
            CraneFrenzySecondsElapsed = 0f;
            SetCranePhase(CombatCranePhase.Frenzied);
            if (CraneHookPhase is CombatCraneHookPhase.Outbound or CombatCraneHookPhase.Holding or CombatCraneHookPhase.Returning)
                BeginCraneHookReturn();
            craneFrenzySound = (CraneFrenzyPlayCount & 1) == 0 ? craneFrenzyFirstSound : craneFrenzySecondSound;
            craneBodyVoice.clip = craneFrenzySound; craneBodyVoice.volume = .7f; craneBodyVoice.Play();
            if (presentationFrozen || GameTimeScaleRuntime.IsPaused) craneBodyVoice.Pause();
            CraneFrenzyPlayCount++;
        }

        private void AdvanceCraneFrenzy(float seconds)
        {
            AdvanceCraneHook(seconds);
            // The released victim and harmless returning metal cannot receive
            // another contact from the interrupted attack.
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
            CraneFrenzySecondsElapsed = Mathf.Min(CraneFatalSpasmSeconds, CraneFrenzySecondsElapsed + seconds);
            craneClock = CraneFrenzySecondsElapsed;
            if (CraneFrenzySecondsElapsed + .000001f >= CraneFatalSpasmSeconds) EndCraneFrenzy();
        }

        private void PresentCraneFrenzy()
        {
            float time = CraneFrenzySecondsElapsed;
            // Compress the authored five-second impulse score into three
            // seconds, with additional bounded opposing limb snaps.
            SampleNpcClip(zombieFrenzy, time * zombieFrenzy.length / CraneFatalSpasmSeconds);
            float weight = Mathf.SmoothStep(0f, 1f, time / .08f) *
                Mathf.SmoothStep(0f, 1f, (CraneFatalSpasmSeconds - time) / .18f);
            for (int i = 0; i < npcAnimationBaseBones.Length; i++)
            {
                Transform bone = npcAnimationBaseBones[i];
                Quaternion delta = craneFrenzyInverseReferences[i] * bone.localRotation;
                // Reuse the authored impulse score, never the porter's shortened
                // anatomy, grounded root sink or planted knee/boot translations.
                float limb = craneFrenzyLimbWeights[i] * weight;
                Quaternion snap = Quaternion.Euler(Mathf.Sin(time * 43f + i) * 20f * limb,
                    Mathf.Sin(time * 31f + i * 2f) * 12f * limb, Mathf.Sin(time * 37f + i) * 24f * limb);
                bone.SetLocalPositionAndRotation(craneFrenzyPositions[i],
                    craneFrenzyRestRotations[i] * Quaternion.Slerp(Quaternion.identity, delta, weight) * snap);
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
            BeginCraneReturnHome();
        }
    }
}
