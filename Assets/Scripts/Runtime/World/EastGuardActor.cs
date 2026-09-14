using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Authored duty body and face; the precinct owns time, movement and dialogue.</summary>
    [DisallowMultipleComponent]
    public sealed class EastGuardActor : MonoBehaviour, ISpeechFaceActor
    {
        [SerializeField] private int index;
        [SerializeField] private VillageResidentPresentation motion;
        [SerializeField] private Renderer faceRenderer;
        [SerializeField] private NpcWardrobe wardrobe;
        [SerializeField] private Transform carrySocket, rifleRoot;
        [SerializeField] private float height, groundOffset;
        private readonly SpeechFaceAtlasPresenter face = new SpeechFaceAtlasPresenter();
        private object speechOwner;
        private SpeechFacePose leasedPose;
        private double lastSeconds;
        public int Index => index;
        public VillageResidentPresentation Motion => motion;
        public Renderer FaceRenderer => faceRenderer;
        public NpcWardrobe Wardrobe => wardrobe;
        public Transform CarrySocket => carrySocket;
        public Transform RifleRoot => rifleRoot;
        public float Height => height;
        public float GroundOffset => groundOffset;
        public SpeechFacePose CurrentSpeechFace { get; private set; }
        public bool IsFaceReady => face.IsConfigured;
        public SpeechFaceProfile FaceProfile => index == 0 ? SpeechFaceProfile.EastGuardSenior : SpeechFaceProfile.EastGuardJunior;

        public void Configure(int guardIndex, VillageResidentPresentation body, Renderer illustratedFace,
            Transform socket, Transform rifle, float authoredHeight, float authoredGroundOffset)
        {
            if (guardIndex < 0 || guardIndex > 1) throw new ArgumentOutOfRangeException(nameof(guardIndex));
            index = guardIndex; motion = body; faceRenderer = illustratedFace;
            wardrobe = GetComponent<NpcWardrobe>(); carrySocket = socket; rifleRoot = rifle;
            height = authoredHeight; groundOffset = authoredGroundOffset;
            Initialize();
        }

        public void Initialize()
        {
            if (motion == null || faceRenderer == null || wardrobe == null || carrySocket == null || rifleRoot == null ||
                !face.Configure(faceRenderer, EastGuardAssetProvider.Folder + EastGuardAssetProvider.Name(index) + "FaceAtlas", false))
                throw new InvalidOperationException("East guard requires his own rig, outfit, slung rifle and painted face.");
            var block = new MaterialPropertyBlock();
            faceRenderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", Color.white); block.SetColor("_Color", Color.white);
            faceRenderer.SetPropertyBlock(block);
            ApplyFaceAt(lastSeconds);
        }

        public void ApplyAt(double seconds, float speed, float gaitCycles,
            Vector3? lookAt = null, NpcSpeechBubbleView bubbles = null, float listeningWeight = 0f)
        {
            if (speed <= .01f)
                motion.ApplyIdleVariation((float)seconds, true, listeningWeight, lookAt);
            else
                motion.ApplyLocomotion(speed, false, gaitCycles * motion.ClipLength(VillageResidentAction.Walk), lookAt);
            ApplyFaceAt(seconds, bubbles);
        }

        public void ApplyFaceAt(double seconds, NpcSpeechBubbleView bubbles = null)
        {
            if (!face.IsConfigured) return;
            lastSeconds = seconds;
            SpeechFacePose pose = speechOwner != null ? leasedPose :
                bubbles != null && bubbles.TryGetSpeechFaceSample(motion, out SpeechFaceSample sample)
                    ? SpeechFaceAnimation.Resolve(sample, FaceProfile, seconds)
                    : SpeechFaceAnimation.ResolveListening(FaceProfile, seconds);
            if (face.Apply(pose)) CurrentSpeechFace = pose;
        }

        public bool TrySetSpeechFace(object owner, SpeechFacePose pose)
        {
            if (owner == null || !isActiveAndEnabled || !face.IsConfigured ||
                speechOwner != null && !ReferenceEquals(owner, speechOwner)) return false;
            speechOwner = owner; leasedPose = pose; ApplyFaceAt(lastSeconds); return true;
        }

        public void ReleaseSpeechFace(object owner)
        {
            if (owner == null || !ReferenceEquals(owner, speechOwner)) return;
            speechOwner = null; ApplyFaceAt(lastSeconds);
        }

        private void OnEnable()
        {
            if (motion != null && faceRenderer != null) Initialize();
            if (Application.isPlaying) NpcFootstepSources.Register(transform);
        }
        private void OnDisable()
        {
            speechOwner = null; face.Clear();
            if (Application.isPlaying) NpcFootstepSources.Unregister(transform);
        }
    }
}
