using UnityEngine;

namespace BarPromenade
{
    /// <summary>Uses the same worker speech textures as the kiosk seller.</summary>
    internal sealed class KioskVisitorFace : ISpeechFaceActor
    {
        private readonly DefaultNpcAppearance appearance;
        private readonly Renderer face;
        private readonly SpeechFaceAtlasPresenter presenter = new();
        private readonly MaterialPropertyBlock ordinary = new();
        private object owner;
        private Texture2D sourceAtlas;

        internal KioskVisitorFace(VillageResidentPresentation actor)
        {
            appearance = actor.GetComponent<DefaultNpcAppearance>();
            face = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "GEO_FaceSurface").GetComponent<Renderer>();
        }

        public bool TrySetSpeechFace(object requester, SpeechFacePose pose)
        {
            if (!face || !appearance || owner != null && !ReferenceEquals(owner, requester)) return false;
            if (owner == null)
            {
                Texture2D atlas = appearance.CurrentAtlas;
                if (sourceAtlas != atlas || !presenter.IsConfigured)
                {
                    if (!presenter.Configure(face, "Dialogue/Faces/Workers/" + atlas.name,
                        false, new Rect(.75f, 0f, .25f, .25f))) return false;
                    sourceAtlas = atlas;
                }
                face.GetPropertyBlock(ordinary);
                owner = requester;
            }
            return presenter.Apply(pose);
        }

        public void ReleaseSpeechFace(object requester)
        {
            if (!ReferenceEquals(owner, requester)) return;
            if (face) face.SetPropertyBlock(ordinary);
            ordinary.Clear();
            owner = null;
        }
    }
}
