using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>An ordinary wall picture; the shared inspection owns approach, framing and cleanup.</summary>
    public static class HomeWallPainting
    {
        public const string PromptKey = "interaction.inspect_painting";
        public const string TexturePath = "Home/Textures/HomeWeaverPaintingAlbedo";
        public const string FrameName = "Home Weaver Painting Frame";
        public const string ImageName = "Home Weaver Painting Image";
        public static readonly Vector3 FrameCenter = new Vector3(-.97f, 1.98f, .745f);
        public static readonly Vector3 FrameSize = new Vector3(1.40f, .82f, .045f);
        public static readonly Vector3 ImageSize = new Vector3(1.34f, 1.34f * 941f / 1672f, .006f);
        // Scene loading unloads unused assets after composition. A texture
        // referenced only by an MPB is not a retained managed resource.
        private static Texture2D paintingTexture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetResources() => paintingTexture = null;

        public static NarrativeInteraction Build(Transform room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            Texture2D texture = paintingTexture != null
                ? paintingTexture
                : paintingTexture = Resources.Load<Texture2D>(TexturePath);
            if (texture == null) throw new InvalidOperationException("The Home wall painting needs its supplied image.");
            var subject = new GameObject("Home Wall Painting");
            subject.transform.SetParent(room, false);
            HomeAuthoredVisualFactory.CreateBox(FrameName, subject.transform, FrameCenter,
                FrameSize, new Color(.18f, .11f, .065f), collider: false);
            GameObject image = HomeAuthoredVisualFactory.CreateBox(ImageName, subject.transform,
                new Vector3(FrameCenter.x, FrameCenter.y, .718f), ImageSize, Color.white, collider: false);
            Renderer renderer = image.GetComponent<Renderer>();
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture("_BaseMap", texture);
            properties.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, 0f));
            properties.SetColor("_BaseColor", Color.white);
            properties.SetColor("_Color", Color.white);
            properties.SetFloat("_Smoothness", .08f);
            renderer.SetPropertyBlock(properties);
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            Bounds focus = renderer.bounds;
            focus.Encapsulate(subject.transform.Find(FrameName).GetComponent<Renderer>().bounds);
            Vector3 floor = room.TransformPoint(new Vector3(FrameCenter.x, 0f, -1.25f));
            Quaternion facing = room.rotation;
            var staging = NarrativeStagingPlan.Standing(floor, facing, focus, room.right,
                NarrativeCameraMode.ObjectCloseUp, -room.forward, authoredFocusBoundsOnly: true);
            var definition = new NarrativeInteractionDefinition("home-wall-painting", PromptKey,
                Array.Empty<NarrativePage>(), silent: true);
            NarrativeInteraction inspection = subject.AddComponent<NarrativeInteraction>();
            inspection.Configure(definition, subject.transform, staging,
                room.TransformPoint(new Vector3(FrameCenter.x, .85f, -.80f)));
            return inspection;
        }
    }
}
