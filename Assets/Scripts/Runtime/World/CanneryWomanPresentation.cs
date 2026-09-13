using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The line operator's painted face. Delivery and body clocks remain with their shared owners.</summary>
    [DisallowMultipleComponent]
    public sealed class CanneryWomanPresentation : MonoBehaviour, ISpeechFaceActor
    {
        public const int WorkerSlot = 2;
        public const int SoftenedCell = 30, SmileCell = 31;
        public const string FaceResourcePath = "City/Cannery/Woman/CanneryWomanFaceAtlas";
        [SerializeField] private VillageResidentPresentation motion;
        [SerializeField] private Renderer faceRenderer;
        [SerializeField] private CanneryWomanHair hair;
        [SerializeField] private CanneryWomanWardrobe wardrobe;
        private readonly SpeechFaceAtlasPresenter face = new SpeechFaceAtlasPresenter();
        private object speechOwner;
        private SpeechFacePose leasedPose;
        private double smileStart = double.PositiveInfinity, lastSeconds;
        public VillageResidentPresentation Motion => motion;
        public Renderer FaceRenderer => faceRenderer;
        public SpeechFacePose CurrentSpeechFace { get; private set; }
        public int CurrentFaceCell { get; private set; }
        public bool IsSmiling => CurrentFaceCell == SmileCell;
        public bool IsFaceReady => face.IsConfigured;
        public CanneryWomanHair Hair => hair;
        public CanneryWomanWardrobe Wardrobe => wardrobe;

        public void Configure(VillageResidentPresentation body, Renderer illustratedFace)
        {
            motion = body; faceRenderer = illustratedFace;
            hair = GetComponent<CanneryWomanHair>();
            wardrobe = GetComponent<CanneryWomanWardrobe>();
            Initialize();
        }

        public void Initialize()
        {
            if (motion == null || faceRenderer == null ||
                !face.Configure(faceRenderer, FaceResourcePath, false))
                throw new InvalidOperationException("The cannery woman needs her authored rig and 512 x 256 painted face atlas.");
            var block = new MaterialPropertyBlock();
            faceRenderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", Color.white); block.SetColor("_Color", Color.white);
            faceRenderer.SetPropertyBlock(block);
            ApplyFaceAt(lastSeconds);
        }

        public void ApplyFaceAt(double seconds, NpcSpeechBubbleView bubbles = null)
        {
            if (!face.IsConfigured) return;
            lastSeconds = seconds;
            SpeechFacePose pose = speechOwner != null ? leasedPose :
                bubbles != null && bubbles.TryGetSpeechFaceSample(motion, out SpeechFaceSample sample)
                    ? SpeechFaceAnimation.Resolve(sample, SpeechFaceProfile.CanneryWoman, seconds)
                    : SpeechFaceAnimation.ResolveListening(SpeechFaceProfile.CanneryWoman, seconds);
            int cell = pose.AtlasCell;
            double warmth = seconds - smileStart;
            if (speechOwner == null && pose.Mouth == SpeechMouthPose.Closed &&
                pose.Expression != SpeechFaceExpression.Blink && pose.Expression != SpeechFaceExpression.HalfBlink &&
                warmth >= 0d && warmth < 3.1d)
                cell = warmth < .55d || warmth >= 2.35d ? SoftenedCell : SmileCell;
            if (face.ApplyCell(cell)) { CurrentSpeechFace = pose; CurrentFaceCell = cell; }
        }

        /// <summary>Only two accepted colleague replies invite warmth; silent seeks never call this.</summary>
        public void ObserveReply(string key, double now, double lineDuration)
        {
            if (key == "city.cannery.wait.07.b" || key == "city.cannery.wait.10.b")
                smileStart = now + Math.Max(.8d, lineDuration - SpeechDelivery.ReadingTailSeconds);
        }

        public void ResetSocialFace()
        {
            smileStart = double.PositiveInfinity;
            speechOwner = null;
            ApplyFaceAt(lastSeconds);
        }

        public bool TrySetSpeechFace(object owner, SpeechFacePose pose)
        {
            if (owner == null || !isActiveAndEnabled || !face.IsConfigured ||
                speechOwner != null && !ReferenceEquals(speechOwner, owner)) return false;
            speechOwner = owner; leasedPose = pose;
            ApplyFaceAt(lastSeconds);
            return true;
        }

        public void ReleaseSpeechFace(object owner)
        {
            if (owner == null || !ReferenceEquals(owner, speechOwner)) return;
            speechOwner = null;
            ApplyFaceAt(lastSeconds);
        }

        private void OnEnable() { if (motion != null && faceRenderer != null) Initialize(); }
        private void OnDisable() => ResetSocialFace();
    }
}
