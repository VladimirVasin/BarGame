using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Authored leather and sole flex on the existing foot/shin rig.</summary>
    [DefaultExecutionOrder(408)]
    [DisallowMultipleComponent]
    public sealed class PlayerBootDeformation : MonoBehaviour
    {
        [Serializable]
        public sealed class ShapeBinding
        {
            public SkinnedMeshRenderer Renderer;
            public int Toe15, Toe30, Toe45, Dorsiflex, Plantarflex;
        }

        [Serializable]
        public sealed class FootBinding
        {
            public FootSide Side;
            public Transform Foot, Shin;
            public Vector3 FootForwardLocal, FootUpLocal, ShinForwardLocal, ShinRightLocal;
            public Vector3 BallContactLocal, BallPivotLocal;
            public float DorsiflexMaximumDegrees, PlantarflexMaximumDegrees;
            public ShapeBinding[] Shapes = Array.Empty<ShapeBinding>();
        }

        [SerializeField] private FootBinding[] bindings = Array.Empty<FootBinding>();
        private Player3DCharacterPresentation presentation;
        public IReadOnlyList<FootBinding> Bindings => bindings;
        public bool HasBindings => bindings.Length == 2;

        /// <summary>Authoring provides measured rest frames and serialized renderer/shape references.</summary>
        public void Configure(FootBinding[] authored)
        {
            if (authored == null || authored.Length != 2)
                throw new ArgumentException("Footwear requires both authored feet.", nameof(authored));
            bindings = authored;
            ApplyPose(FootGroundSample.None, FootGroundSample.None);
        }

        public void BindPresentation(Player3DCharacterPresentation value) => presentation = value;

        public void ApplyPose()
        {
            bool supported = presentation != null && !presentation.RagdollPoseActive && presentation.FootIkBlend > .001f;
            ApplyPose(supported ? presentation.LeftFootGround : FootGroundSample.None,
                supported ? presentation.RightFootGround : FootGroundSample.None);
        }

        /// <summary>Uses completed foot transforms and existing per-foot support knowledge; never writes bones.</summary>
        public void ApplyPose(FootGroundSample left, FootGroundSample right)
        {
            foreach (FootBinding foot in bindings)
            {
                if (foot.Foot == null || foot.Shin == null) continue;
                Vector3 forward = foot.Foot.TransformDirection(foot.FootForwardLocal);
                Vector3 up = foot.Foot.TransformDirection(foot.FootUpLocal);
                float ankle = -Pitch(foot.Shin.TransformDirection(foot.ShinForwardLocal), forward,
                    foot.Shin.TransformDirection(foot.ShinRightLocal));
                float dorsiflex = Mathf.Clamp01(ankle / foot.DorsiflexMaximumDegrees) * 100f;
                float plantarflex = Mathf.Clamp01(-ankle / foot.PlantarflexMaximumDegrees) * 100f;
                FootGroundSample support = foot.Side == FootSide.Left ? left : right;
                float roll = SupportedRoll(foot, forward, up, support);
                ToeWeights(roll, out float toe15, out float toe30, out float toe45);
                foreach (ShapeBinding shape in foot.Shapes)
                {
                    if (shape.Renderer == null) continue;
                    if (shape.Dorsiflex >= 0) shape.Renderer.SetBlendShapeWeight(shape.Dorsiflex, dorsiflex);
                    if (shape.Plantarflex >= 0) shape.Renderer.SetBlendShapeWeight(shape.Plantarflex, plantarflex);
                    shape.Renderer.SetBlendShapeWeight(shape.Toe15, toe15);
                    shape.Renderer.SetBlendShapeWeight(shape.Toe30, toe30);
                    shape.Renderer.SetBlendShapeWeight(shape.Toe45, toe45);
                }
            }
        }

        private float SupportedRoll(FootBinding foot, Vector3 forward, Vector3 up, FootGroundSample support)
        {
            if (!support.HasSurface || !support.HasToeSurface) return 0f;
            Vector3 normal = support.Normal.normalized;
            Vector3 surfaceForward = Vector3.ProjectOnPlane(forward, normal);
            if (surfaceForward.sqrMagnitude < .000001f || Vector3.Dot(up.normalized, normal) < .5f) return 0f;
            Vector3 right = Vector3.Cross(normal, surfaceForward).normalized;
            // Turning the toes out or rolling around the foot's length cannot become a toe curl.
            if (Vector3.Dot(Vector3.Cross(up, forward).normalized, right) < .819152f) return 0f;
            float angle = Mathf.Clamp(Pitch(surfaceForward, forward, right), 0f, 45f);
            float clearance = presentation != null && presentation.Layer.HasSoleClearance
                ? presentation.Layer.SoleClearance : 0f;
            Vector3 pivot = foot.Foot.TransformPoint(foot.BallPivotLocal);
            Vector3 ball = foot.Foot.TransformPoint(foot.BallContactLocal);
            // Test contact on the fully rolled forefoot plane. Its hinge is
            // above the underside, so grounding the completed sole carries
            // the undeformed ball up slightly; that is still full contact.
            ball = pivot + Quaternion.AngleAxis(-angle, Vector3.Cross(up, forward).normalized) * (ball - pivot);
            float surfaceY = support.ToeY;
            if (support.HasToeSupportPoint && normal.y > .0001f)
            {
                Vector3 delta = ball - support.ToeSupportPoint;
                surfaceY -= (normal.x * delta.x + normal.z * delta.z) / normal.y;
            }
            float gap = ball.y - surfaceY - clearance;
            if (gap < -.03f) return 0f;
            float contact = 1f - Mathf.InverseLerp(.005f, .04f, gap);
            return angle * contact;
        }

        private static float Pitch(Vector3 neutral, Vector3 current, Vector3 axis)
        {
            neutral = Vector3.ProjectOnPlane(neutral, axis);
            current = Vector3.ProjectOnPlane(current, axis);
            return neutral.sqrMagnitude > .000001f && current.sqrMagnitude > .000001f
                ? Vector3.SignedAngle(neutral, current, axis) : 0f;
        }

        public static void ToeWeights(float degrees, out float toe15, out float toe30, out float toe45)
        {
            float angle = Mathf.Clamp(degrees, 0f, 45f);
            toe15 = toe30 = toe45 = 0f;
            if (angle <= 15f) toe15 = angle / 15f * 100f;
            else if (angle <= 30f)
            {
                toe30 = (angle - 15f) / 15f * 100f;
                toe15 = 100f - toe30;
            }
            else
            {
                toe45 = (angle - 30f) / 15f * 100f;
                toe30 = 100f - toe45;
            }
        }

        /// <summary>A reflection displays the source's support decision and final flex, without probing again.</summary>
        public void CopyPoseTo(PlayerBootDeformation target)
        {
            if (target == null || target.bindings.Length != bindings.Length) return;
            for (int side = 0; side < bindings.Length; side++)
            {
                FootBinding source = bindings[side], destination = target.bindings[side];
                if (source.Side != destination.Side || source.Shapes.Length != destination.Shapes.Length)
                    throw new InvalidOperationException("Reflected footwear lost its authored binding order.");
                for (int index = 0; index < source.Shapes.Length; index++)
                {
                    ShapeBinding from = source.Shapes[index], to = destination.Shapes[index];
                    if (from.Renderer == null || to.Renderer == null) continue;
                    to.Renderer.SetBlendShapeWeight(to.Toe15, from.Renderer.GetBlendShapeWeight(from.Toe15));
                    to.Renderer.SetBlendShapeWeight(to.Toe30, from.Renderer.GetBlendShapeWeight(from.Toe30));
                    to.Renderer.SetBlendShapeWeight(to.Toe45, from.Renderer.GetBlendShapeWeight(from.Toe45));
                    if (from.Dorsiflex >= 0 && to.Dorsiflex >= 0)
                        to.Renderer.SetBlendShapeWeight(to.Dorsiflex, from.Renderer.GetBlendShapeWeight(from.Dorsiflex));
                    if (from.Plantarflex >= 0 && to.Plantarflex >= 0)
                        to.Renderer.SetBlendShapeWeight(to.Plantarflex, from.Renderer.GetBlendShapeWeight(from.Plantarflex));
                }
            }
        }

        private void LateUpdate() => ApplyPose();
    }
}
