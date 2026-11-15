using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The skinned legs carry the coat; only the free hems pick up an outdoor breeze.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(310)]
    public sealed class LastRouteFerrymanCoat : MonoBehaviour
    {
        private const string HemShape = "FerrymanHemBreeze";
        private const float MaximumWindWeight = 62f;
        private LastRouteFerrymanPresentation ferryman;
        private SkinnedMeshRenderer left, right;
        private int leftShape, rightShape;
        private float leftWeight, rightWeight, refresh;
        private Vector3 breeze;

        public SkinnedMeshRenderer LeftHem => left;
        public SkinnedMeshRenderer RightHem => right;
        public bool IsInitialized => left != null && right != null;

        internal void Initialize(LastRouteFerrymanPresentation owner, NpcWardrobe wardrobe)
        {
            ferryman = owner;
            foreach (var item in wardrobe.Items)
            {
                if (item.Id != DefaultNpcPopulation.FerrymanCoatItem) continue;
                foreach (var part in item.Parts)
                {
                    if (!(part.Renderer is SkinnedMeshRenderer skin)) continue;
                    if (skin.name == "CLO_FerrymanCoatHem.L") left = skin;
                    if (skin.name == "CLO_FerrymanCoatHem.R") right = skin;
                }
            }
            if (!IsInitialized)
                throw new InvalidOperationException("Ferryman's coat requires both skinned hems.");
            leftShape = CharacterJointDeformation.FindShape(left.sharedMesh, HemShape);
            rightShape = CharacterJointDeformation.FindShape(right.sharedMesh, HemShape);
            if (leftShape < 0 || rightShape < 0)
                throw new InvalidOperationException("Ferryman's coat lost its free-edge shape.");
        }

        private void LateUpdate()
        {
            if (!IsInitialized || Time.deltaTime <= 0f) return;
            bool outside = ferryman.Phase == LastRouteFerrymanPhase.Waiting ||
                ferryman.Phase == LastRouteFerrymanPhase.WalkingToDoor ||
                ferryman.Phase == LastRouteFerrymanPhase.WalkingToBonnet ||
                ferryman.Phase == LastRouteFerrymanPhase.Dismounting ||
                ferryman.Phase == LastRouteFerrymanPhase.Mounting;
            if (outside && (left.isVisible || right.isVisible))
            {
                refresh -= Time.deltaTime;
                if (refresh <= 0f)
                {
                    WindSample wind = GameWeatherRules.EvaluateCurrentWind();
                    breeze = wind.HorizontalDirection * wind.Strength01;
                    refresh = .16f;
                }
                float lateral = Vector3.Dot(breeze, transform.right);
                float flutter = .79f + .21f * Mathf.Sin(Time.time * 3.1f);
                Write(Mathf.Max(0f, -.2f - lateral) * flutter * MaximumWindWeight,
                    Mathf.Max(0f, lateral - .2f) * flutter * MaximumWindWeight);
            }
            else
            {
                refresh = 0f;
                Write(0f, 0f);
            }
        }

        private void Write(float leftTarget, float rightTarget)
        {
            float step = Time.deltaTime * 230f;
            float nextLeft = Mathf.MoveTowards(leftWeight, leftTarget, step);
            float nextRight = Mathf.MoveTowards(rightWeight, rightTarget, step);
            if (nextLeft != leftWeight) left.SetBlendShapeWeight(leftShape, nextLeft);
            if (nextRight != rightWeight) right.SetBlendShapeWeight(rightShape, nextRight);
            leftWeight = nextLeft;
            rightWeight = nextRight;
        }

        private void OnDisable()
        {
            if (!IsInitialized || leftShape < 0 || rightShape < 0) return;
            if (leftWeight != 0f) left.SetBlendShapeWeight(leftShape, 0f);
            if (rightWeight != 0f) right.SetBlendShapeWeight(rightShape, 0f);
            leftWeight = rightWeight = 0f;
            refresh = 0f;
        }
    }
}
