using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Painted expressions read the shared delivery; the factory owns every clock and gesture.</summary>
    [DisallowMultipleComponent]
    public sealed class CanneryReceiverPresentation : MonoBehaviour, ISpeechFaceActor
    {
        public const int WorkerSlot = 0;
        public const int SmirkCell = 30, GrinCell = 31;
        public const string WorkwearId = "cannery_receiver_workwear";
        public const string FaceResourcePath = "City/Cannery/Receiver/CanneryReceiverFaceAtlas";
        [SerializeField] private VillageResidentPresentation motion;
        [SerializeField] private Renderer faceRenderer;
        [SerializeField] private Transform glassesRoot;
        [SerializeField] private NpcWardrobe wardrobe;
        private readonly SpeechFaceAtlasPresenter face = new SpeechFaceAtlasPresenter();
        private object speechOwner;
        private SpeechFacePose leasedPose;
        private double smirkStart = double.PositiveInfinity, lastSeconds;
        public VillageResidentPresentation Motion => motion;
        public Renderer FaceRenderer => faceRenderer;
        public Transform GlassesRoot => glassesRoot;
        public NpcWardrobe Wardrobe => wardrobe;
        public SpeechFacePose CurrentSpeechFace { get; private set; }
        public int CurrentFaceCell { get; private set; }
        public bool IsFaceReady => face.IsConfigured;

        public void Configure(VillageResidentPresentation body, Renderer illustratedFace, Transform glasses)
        {
            motion = body; faceRenderer = illustratedFace; glassesRoot = glasses;
            wardrobe = GetComponent<NpcWardrobe>();
            Initialize();
        }

        public void Initialize()
        {
            if (motion == null || faceRenderer == null || glassesRoot == null ||
                !face.Configure(faceRenderer, FaceResourcePath, false))
                throw new InvalidOperationException("The receiver needs his authored rig, glasses and 512 x 256 painted face atlas.");
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
                    ? SpeechFaceAnimation.Resolve(sample, SpeechFaceProfile.CanneryReceiver, seconds)
                    : SpeechFaceAnimation.ResolveListening(SpeechFaceProfile.CanneryReceiver, seconds);
            int cell = pose.AtlasCell;
            double smirk = seconds - smirkStart;
            if (speechOwner == null && pose.Mouth == SpeechMouthPose.Closed &&
                pose.Expression != SpeechFaceExpression.Blink && pose.Expression != SpeechFaceExpression.HalfBlink &&
                smirk >= 0d && smirk < 2.7d)
                cell = smirk < .45d || smirk >= 1.9d ? SmirkCell : GrinCell;
            if (face.ApplyCell(cell)) { CurrentSpeechFace = pose; CurrentFaceCell = cell; }
        }

        public void ObserveReply(int role, string key, double now, double lineDuration)
        {
            // An actual dry reply earns the expression; reconstruction cannot invent a social beat.
            if (role == WorkerSlot && (key == "city.cannery.wait.06.b" || key == "city.cannery.work.06.b"))
                smirkStart = now + Math.Max(.8d, lineDuration - SpeechDelivery.ReadingTailSeconds);
        }

        public void ResetSocialFace()
        {
            smirkStart = double.PositiveInfinity; speechOwner = null;
            ApplyFaceAt(lastSeconds);
        }

        public bool TrySetSpeechFace(object owner, SpeechFacePose pose)
        {
            if (owner == null || !isActiveAndEnabled || !face.IsConfigured ||
                speechOwner != null && !ReferenceEquals(speechOwner, owner)) return false;
            speechOwner = owner; leasedPose = pose; ApplyFaceAt(lastSeconds); return true;
        }

        public void ReleaseSpeechFace(object owner)
        {
            if (owner == null || !ReferenceEquals(owner, speechOwner)) return;
            speechOwner = null; ApplyFaceAt(lastSeconds);
        }

        private void OnEnable() { if (motion != null && faceRenderer != null) Initialize(); }
        private void OnDisable() => ResetSocialFace();
    }
}
