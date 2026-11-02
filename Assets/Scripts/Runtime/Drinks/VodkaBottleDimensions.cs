using UnityEngine;

namespace BarPromenade
{
    /// <summary>Measured metres for the ordinary half-litre source; shader and collision share this contract.</summary>
    public static class VodkaBottleDimensions
    {
        public const float SourceScale = .65f;
        public const float RadialScale = .75f;
        public const float Height = .460f * SourceScale;
        public const float GripHeight = .215f;
        public const float GripRadius = .0147796875f;
        public const float Width = .153f * SourceScale * RadialScale;
        public const float MouthHeight = .440f * SourceScale;
        public const float CapHeight = .445f * SourceScale;
        public const float NeckGripHeight = GripHeight;
        public const float NeckStartHeight = .175f;
        public const float BodyTopHeight = .150f;
        public const float NeckInnerRadius = GripRadius - .004f * SourceScale * RadialScale;
        public const float BodyRadius = .073f * SourceScale * RadialScale;
        public const float InnerBottom = .012f * SourceScale;
        public const float InnerTop = .436f * SourceScale;
        public const float InnerBoundsRadius = .069f * SourceScale * RadialScale;
        public const float LiquidSurfaceScale = 3.7f / RadialScale;
        public const float LiquidCentre = .224f * SourceScale;
        public const float PassiveLiquidHeight = .280f * SourceScale;
        public const float LowerColliderCentre = .085f;
        public const float LowerColliderHeight = .170f;
        public const float NeckColliderCentre = .2155f;
        public const float NeckColliderRadius = .032f * SourceScale * RadialScale;
        public const float NeckColliderHeight = .141f;
        public const float CentreOfMass = .160f * SourceScale;
        internal static readonly Vector4[] InnerProfile =
        {
            new Vector4(.012f, .060f * RadialScale, 0f, 0f) * SourceScale,
            new Vector4(.020f, .068f * RadialScale, 0f, 0f) * SourceScale,
            new Vector4(BodyTopHeight, .068f * RadialScale * SourceScale, 0f, 0f),
            new Vector4(NeckStartHeight, NeckInnerRadius, 0f, 0f),
            new Vector4(.350f * SourceScale, NeckInnerRadius, 0f, 0f),
            new Vector4(.430f * SourceScale, NeckInnerRadius, 0f, 0f),
            new Vector4(.436f * SourceScale, NeckInnerRadius * .4f + .028f * RadialScale * SourceScale * .6f, 0f, 0f)
        };
    }
}
