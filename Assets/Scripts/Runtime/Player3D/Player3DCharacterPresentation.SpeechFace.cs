namespace BarPromenade
{
    public sealed partial class Player3DCharacterPresentation : ISpeechFaceActor
    {
        private readonly SpeechFaceAtlasPresenter speechFacePresenter = new SpeechFaceAtlasPresenter();
        private object speechFaceOwner;
        private SpeechFacePose speechFacePose;
        public bool HasSpeechFace => speechFaceOwner != null;
        public SpeechFacePose CurrentSpeechFace => speechFacePose;

        public bool TrySetSpeechFace(object owner, SpeechFacePose pose)
        {
            if (owner == null || !isActiveAndEnabled || registry == null || !registry.HasFaceAtlas ||
                (speechFaceOwner != null && !ReferenceEquals(speechFaceOwner, owner)) ||
                (contextualFaceOwner != null && !ReferenceEquals(contextualFaceOwner, owner))) return false;
            if (!speechFacePresenter.IsConfigured && !speechFacePresenter.Configure(
                    registry.FaceAtlas.Renderer, SpeechFaceAtlasResources.HeroPath, true)) return false;
            speechFaceOwner = owner; speechFacePose = pose;
            return speechFacePresenter.Apply(pose, IsMouthSoiledVisible);
        }

        public void ReleaseSpeechFace(object owner)
        {
            if (owner == null || !ReferenceEquals(speechFaceOwner, owner)) return;
            ClearSpeechFace();
            ReapplyFacialPose();
        }

        private void ClearSpeechFace()
        {
            speechFaceOwner = null; speechFacePose = default;
            speechFacePresenter.Clear();
        }
    }
}
