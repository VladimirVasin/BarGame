using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    public sealed partial class Player3DCharacterPresentation
    {
        private const float ColdBlendSeconds = 0.45f;
        private readonly PlayerColdPresentationModel coldModel = new PlayerColdPresentationModel();
        private Func<bool> coldEnvironment;
        private AnimationLayerMixerPlayable coldLayers;
        private AnimationMixerPlayable coldTorso;
        private AnimationMixerPlayable coldArms;
        private AnimationClipPlayable coldTorsoHold;
        private AnimationClipPlayable coldArmsHold;
        private AnimationClipPlayable coldRub;
        private AnimationClipPlayable coldTorsoShiver;
        private AnimationClipPlayable coldArmsShiver;
        private AvatarMask coldTorsoMask;
        private AvatarMask coldArmsMask;
        private float coldBodyWeight;
        private float coldArmWeight;
        private float coldRubWeight;
        private float coldRubTime;
        private PlayerColdBreathEffect coldBreath;

        public float ColdBodyWeight => coldBodyWeight;
        public float ColdArmWeight => coldArmWeight;
        public PlayerColdPresentationModel ColdModel => coldModel;
        public PlayerColdBreathEffect ColdBreath => coldBreath;

        /// <summary>
        /// The exterior root opts in. The ordinary graph remains the sole bone
        /// owner; its full-body action slot still overrides both cold masks.
        /// </summary>
        public void ConfigureCold(Func<bool> isColdExterior, Func<WindSample> wind)
        {
            if (isColdExterior == null || wind == null)
                throw new ArgumentNullException(isColdExterior == null ? nameof(isColdExterior) : nameof(wind));
            if (!graph.IsValid() || registry == null)
                throw new InvalidOperationException("Initialize the hero before configuring cold.");
            if (!TryResolveAnimation("ColdHold", out var hold) ||
                !TryResolveAnimation("ColdShoulderRub", out var rub) ||
                !TryResolveAnimation("ColdShiver", out var shiver))
                throw new InvalidOperationException(
                    "The alpine hero requires ColdHold, ColdShoulderRub and ColdShiver.");

            if (!coldLayers.IsValid())
            {
                coldLayers = AnimationLayerMixerPlayable.Create(graph, 3);
                coldTorso = AnimationMixerPlayable.Create(graph, 2);
                coldArms = AnimationMixerPlayable.Create(graph, 3);
                coldTorsoHold = CreateLocomotionPlayable(hold);
                coldArmsHold = CreateLocomotionPlayable(hold);
                coldRub = CreateLocomotionPlayable(rub);
                coldTorsoShiver = CreateLocomotionPlayable(shiver);
                coldArmsShiver = CreateLocomotionPlayable(shiver);
                coldTorsoHold.SetSpeed(0d);
                coldArmsHold.SetSpeed(0d);
                coldRub.SetSpeed(0d);
                coldTorsoShiver.SetSpeed(0d);
                coldArmsShiver.SetSpeed(0d);
                graph.Connect(coldTorsoHold, 0, coldTorso, 0);
                graph.Connect(coldTorsoShiver, 0, coldTorso, 1);
                graph.Connect(coldArmsHold, 0, coldArms, 0);
                graph.Connect(coldRub, 0, coldArms, 1);
                graph.Connect(coldArmsShiver, 0, coldArms, 2);
                graph.Connect(coldTorso, 0, coldLayers, 1);
                graph.Connect(coldArms, 0, coldLayers, 2);
                InsertOrdinaryPresentation(coldLayers);
                coldTorsoMask = CreateColdMask(false);
                coldArmsMask = CreateColdMask(true);
                coldLayers.SetLayerMaskFromAvatarMask(1, coldTorsoMask);
                coldLayers.SetLayerMaskFromAvatarMask(2, coldArmsMask);
                coldLayers.SetInputWeight(0, 1f);
            }

            coldEnvironment = isColdExterior;
            if (coldBreath == null)
            {
                var effect = new GameObject("Hero Cold Breath");
                effect.transform.SetParent(transform, false);
                coldBreath = effect.AddComponent<PlayerColdBreathEffect>();
                coldBreath.Initialize(registry.Anchors.Mouth, wind);
            }
            ResetColdPose();
        }

        private AvatarMask CreateColdMask(bool armsOnly)
        {
            Transform animatorRoot = registry.Animator.transform;
            Transform[] transforms = animatorRoot.GetComponentsInChildren<Transform>(true);
            var mask = new AvatarMask { name = armsOnly ? "Hero Cold Arms" : "Hero Cold Torso" };
            mask.transformCount = transforms.Length;
            // The clavicles lift with the self-hug, and must release with its arms.
            Transform left = leftUpperArmBone.parent;
            Transform right = rightUpperArmBone.parent;
            for (int index = 0; index < transforms.Length; index++)
            {
                Transform bone = transforms[index];
                bool arm = bone.IsChildOf(left) || bone.IsChildOf(right);
                bool torso = bone.IsChildOf(registry.Anchors.Spine);
                mask.SetTransformPath(index, ColdTransformPath(bone, animatorRoot));
                mask.SetTransformActive(index, armsOnly ? arm : torso && !arm);
            }
            return mask;
        }

        private static string ColdTransformPath(Transform bone, Transform root)
        {
            if (bone == root) return string.Empty;
            string path = bone.name;
            for (Transform parent = bone.parent; parent != null && parent != root; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }

        private bool ColdOrdinaryAllowed => coldEnvironment != null &&
            isActiveAndEnabled && coldEnvironment() && !IsClipActive &&
            !interactionHandoffLocked && !ragdollPoseActive && !risePose.Active;

        private bool ColdUnwell => !nauseaPose.IsNone || !vomitPose.IsNone;

        private bool ColdProtectiveArmsOwned => ColdUnwell ||
            (balancePose.Weight > 0f && (balancePose.ArmReaction > 0.05f ||
             balancePose.WallReach.Active || balancePose.LeftBrace.Active ||
             balancePose.RightBrace.Active || balancePose.Phase != BalancePhase.Steady));

        private void ReleaseColdForProtectivePose()
        {
            if (!coldLayers.IsValid()) return;
            bool releaseBody = ColdUnwell && coldBodyWeight > 0f;
            bool releaseArms = ColdProtectiveArmsOwned && coldArmWeight > 0f;
            if (!releaseBody && !releaseArms) return;
            if (releaseBody) coldBodyWeight = 0f;
            if (releaseArms) coldArmWeight = 0f;
            // Status controllers can claim a hand after our Update. Restore
            // the ordinary base before their late IK moves it out of the hug;
            // fading the crossed arms underneath that reach causes collisions.
            EvaluateGraph(0f);
        }

        private void AdvanceColdPose(float deltaTime)
        {
            if (!coldLayers.IsValid()) return;
            if (!ColdOrdinaryAllowed)
            {
                ResetColdPose();
                return;
            }

            float step = Mathf.Max(0f, deltaTime) / ColdBlendSeconds;
            // Protective actions take precedence. At ordinary low intoxication
            // the authored hands stay together instead of being spread apart.
            bool unwell = ColdUnwell;
            bool protective = ColdProtectiveArmsOwned;
            coldBodyWeight = unwell ? 0f : Mathf.MoveTowards(coldBodyWeight, 1f, step);
            coldArmWeight = protective ? 0f : Mathf.MoveTowards(coldArmWeight, 1f, step);
            // Running retains the warming gesture. The leg/pelvis channels
            // remain unmasked locomotion, while protective reaches still win.
            coldModel.Step(deltaTime, !protective);
            if (coldModel.IsRubbing)
                coldRubTime = coldModel.RubNormalizedTime;
            // Keep the last stroke sample through a protective release instead
            // of snapping to the hold clip.
            coldRubWeight = Mathf.MoveTowards(coldRubWeight,
                coldModel.RubWeight01, Mathf.Max(0f, deltaTime) / 0.2f);
        }

        private void SampleColdPose()
        {
            if (!coldLayers.IsValid()) return;
            // Entry/exit sampling also calls this with zero time: a newly
            // acquired handoff cannot show yesterday's cold pose for one frame.
            if (!ColdOrdinaryAllowed) ResetColdPose();
            coldTorsoHold.SetTime(coldModel.BreathPhase01 * PlayerColdPresentationModel.BreathCycleSeconds);
            coldArmsHold.SetTime(coldModel.BreathPhase01 * PlayerColdPresentationModel.BreathCycleSeconds);
            coldRub.SetTime(coldRubTime * PlayerColdPresentationModel.ShoulderRubDurationSeconds);
            double shiverTime = coldModel.ShiverNormalizedTime * PlayerColdPresentationModel.ShiverDurationSeconds;
            coldTorsoShiver.SetTime(shiverTime);
            coldArmsShiver.SetTime(shiverTime);
            // A late protective reach suppresses the whole shiver immediately.
            // Its original cold hold can still yield through the existing masks.
            float shiverWeight = ColdProtectiveArmsOwned ? 0f : coldModel.GetShiverWeight(
                GameSessionState.IsInventoryItemEquipped(InventoryItemId.Scarf));
            shiverWeight = Mathf.Min(shiverWeight, 1f - coldRubWeight);
            coldTorso.SetInputWeight(0, 1f - shiverWeight);
            coldTorso.SetInputWeight(1, shiverWeight);
            coldArms.SetInputWeight(0, 1f - coldRubWeight - shiverWeight);
            coldArms.SetInputWeight(1, coldRubWeight);
            coldArms.SetInputWeight(2, shiverWeight);
            coldLayers.SetInputWeight(1, coldBodyWeight);
            coldLayers.SetInputWeight(2, coldArmWeight);
        }

        private void UpdateColdBreath()
        {
            if (coldBreath != null)
                coldBreath.SetBreath(coldModel.ExhaleEnvelope01,
                    ColdOrdinaryAllowed && nauseaPose.IsNone && vomitPose.IsNone);
        }

        private void ResetColdPose()
        {
            coldBodyWeight = coldArmWeight = 0f;
            coldRubWeight = coldRubTime = 0f;
            coldModel.Reset();
            if (coldLayers.IsValid())
            {
                coldLayers.SetInputWeight(1, 0f);
                coldLayers.SetInputWeight(2, 0f);
            }
            if (coldBreath != null) coldBreath.StopAndClear();
        }

        private void DisposeColdGraph()
        {
            ResetColdPose();
            coldEnvironment = null;
            if (coldTorsoMask != null) Destroy(coldTorsoMask);
            if (coldArmsMask != null) Destroy(coldArmsMask);
            coldTorsoMask = coldArmsMask = null;
            coldLayers = default;
            coldTorso = default;
            coldArms = default;
            if (coldBreath != null) Destroy(coldBreath.gameObject);
            coldBreath = null;
        }
    }
}
