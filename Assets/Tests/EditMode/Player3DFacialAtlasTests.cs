using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.EditMode
{
    public sealed class Player3DFacialAtlasTests
    {
        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransformId =
            Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");

        [Test]
        public void Binding_RequiresAndMapsEveryCanonicalExpression()
        {
            GameObject face = new GameObject("Face Surface");
            Texture2D texture = new Texture2D(256, 256);
            try
            {
                Renderer renderer = face.AddComponent<MeshRenderer>();
                Player3DFaceAtlasCell[] cells = CreateCanonicalCells();
                var binding = new Player3DFaceAtlasBinding(
                    renderer,
                    texture,
                    4,
                    4,
                    cells);

                Assert.That(binding.IsConfigured, Is.True);
                Assert.That(
                    binding.TryGetTextureTransform(
                        PlayerFacialExpression.Tense,
                        out Vector4 transform),
                    Is.True);
                Assert.That(
                    transform,
                    Is.EqualTo(new Vector4(0.25f, 0.25f, 0f, 0.25f)));

                var incomplete = new Player3DFaceAtlasBinding(
                    renderer,
                    texture,
                    4,
                    4,
                    new[] { cells[0] });
                Assert.That(incomplete.IsConfigured, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(face);
            }
        }

        [Test]
        public void AnimationBinding_SelectsLastAuthoredKeyAtSampleTime()
        {
            AnimationClip clip = new AnimationClip();
            try
            {
                var binding = new Player3DAnimationBinding(
                    "BedEnter",
                    "bed",
                    clip,
                    1f,
                    false,
                    new[]
                    {
                        new Player3DFacialExpressionKey(
                            0f,
                            PlayerFacialExpression.Neutral),
                        new Player3DFacialExpressionKey(
                            0.6f,
                            PlayerFacialExpression.HalfBlink),
                        new Player3DFacialExpressionKey(
                            0.8f,
                            PlayerFacialExpression.ClosedBlink)
                    });

                AssertExpression(
                    binding,
                    0.59f,
                    PlayerFacialExpression.Neutral);
                AssertExpression(
                    binding,
                    0.6f,
                    PlayerFacialExpression.HalfBlink);
                AssertExpression(
                    binding,
                    1f,
                    PlayerFacialExpression.ClosedBlink);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void AnimationBinding_LeavesLegacyClipsOptional()
        {
            AnimationClip clip = new AnimationClip();
            try
            {
                var binding = new Player3DAnimationBinding(
                    "Legacy",
                    "context",
                    clip,
                    1f,
                    false);

                Assert.That(
                    binding.TryGetFacialExpression(0.5f, out _),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void Presenter_SelectsAtlasCellWithoutCloningSharedMaterial()
        {
            GameObject face = new GameObject("Face Surface");
            Texture2D texture = new Texture2D(256, 256);
            Shader shader = Shader.Find("Bar Promenade/PS1 Lit");
            Assert.That(shader, Is.Not.Null);
            Material material = new Material(shader);
            try
            {
                Renderer renderer = face.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                var presenter = new Player3DFaceAtlasPresenter();
                presenter.Configure(new Player3DFaceAtlasBinding(
                    renderer,
                    texture,
                    4,
                    4,
                    CreateCanonicalCells()));

                Assert.That(
                    presenter.Apply(PlayerFacialExpression.Tense),
                    Is.True);
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetTexture(BaseMapId),
                    Is.SameAs(texture));
                Assert.That(
                    properties.GetVector(BaseMapTransformId),
                    Is.EqualTo(new Vector4(
                        0.25f,
                        0.25f,
                        0f,
                        0.25f)));
                Assert.That(renderer.sharedMaterial, Is.SameAs(material));

                presenter.Reset();
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetTexture(BaseMapId),
                    Is.Null);
                Assert.That(
                    properties.GetVector(BaseMapTransformId),
                    Is.EqualTo(Vector4.zero));
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(face);
            }
        }

        [Test]
        public void RegistryOnEnable_BootstrapsNeutralAtlasWithoutPresentation()
        {
            GameObject face = new GameObject("Face Surface");
            Texture2D texture = new Texture2D(256, 256);
            GameObject root = new GameObject("Hero V2 Registry");
            try
            {
                Renderer renderer = face.AddComponent<MeshRenderer>();
                face.transform.SetParent(root.transform, false);
                Player3DAssetRegistry registry =
                    root.AddComponent<Player3DAssetRegistry>();
                registry.Configure(
                    null,
                    root.transform,
                    new[] { renderer },
                    new[]
                    {
                        new Player3DMeshBinding(
                            "GEO_FaceSurface",
                            "facial_atlas",
                            "head",
                            "Body",
                            string.Empty,
                            "MAT_FaceAtlas",
                            renderer,
                            root.transform,
                            Color.white)
                    },
                    Array.Empty<Player3DAnatomicalPartBinding>(),
                    Array.Empty<Player3DAnimationBinding>(),
                    default,
                    default,
                    "test",
                    "apose",
                    2,
                    "test",
                    new Player3DFaceAtlasBinding(
                        renderer,
                        texture,
                        4,
                        4,
                        CreateCanonicalCells()));

                // Configure runs after AddComponent's first OnEnable, just as
                // the deterministic prefab builder does. Applying the
                // registry contract must still make a directly instantiated
                // V2 face readable before a presentation is initialized.
                registry.ApplyPalette();

                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetTexture(BaseMapId),
                    Is.SameAs(texture));
                Assert.That(
                    properties.GetVector(BaseMapTransformId),
                    Is.EqualTo(new Vector4(0.25f, 0.25f, 0f, 0f)));
                Assert.That(
                    properties.GetColor(BaseColorId),
                    Is.EqualTo(Color.white));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void Presenter_FallsBackWhenACellIsMissing()
        {
            GameObject face = new GameObject("Face Surface");
            Texture2D texture = new Texture2D(256, 256);
            Shader shader = Shader.Find("Bar Promenade/PS1 Lit");
            Assert.That(shader, Is.Not.Null);
            Material material = new Material(shader);
            try
            {
                Renderer renderer = face.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                // An atlas built before the drink's faces: the five cells
                // the runtime always had and nothing else.
                Player3DFaceAtlasCell[] all = CreateCanonicalCells();
                var five = new Player3DFaceAtlasCell[5];
                Array.Copy(all, five, 5);
                var presenter = new Player3DFaceAtlasPresenter();
                presenter.Configure(new Player3DFaceAtlasBinding(
                    renderer,
                    texture,
                    4,
                    4,
                    five));

                // Grimace stands in as Tense (cell 0,1), Drowsy as HalfBlink
                // (cell 1,0), Slack and Glazed as Neutral (cell 0,0).
                Assert.That(presenter.Apply(PlayerFacialExpression.Grimace), Is.True);
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetVector(BaseMapTransformId),
                    Is.EqualTo(new Vector4(0.25f, 0.25f, 0f, 0.25f)));
                Assert.That(presenter.Apply(PlayerFacialExpression.Drowsy), Is.True);
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetVector(BaseMapTransformId),
                    Is.EqualTo(new Vector4(0.25f, 0.25f, 0.25f, 0f)));
                Assert.That(presenter.Apply(PlayerFacialExpression.Slack), Is.True);
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetVector(BaseMapTransformId),
                    Is.EqualTo(new Vector4(0.25f, 0.25f, 0f, 0f)));

                // With the full atlas the drink's own cell is used.
                presenter.Configure(new Player3DFaceAtlasBinding(
                    renderer,
                    texture,
                    4,
                    4,
                    all));
                Assert.That(presenter.Apply(PlayerFacialExpression.Grimace), Is.True);
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetVector(BaseMapTransformId),
                    Is.EqualTo(new Vector4(0.25f, 0.25f, 0f, 0.5f)));
                Assert.That(
                    PlayerFacialExpressionRules.Fallback(PlayerFacialExpression.Tense),
                    Is.EqualTo(PlayerFacialExpression.Neutral));
                Assert.That(PlayerFacialExpressionRules.IsCanonical(PlayerFacialExpression.Tense), Is.True);
                Assert.That(PlayerFacialExpressionRules.IsCanonical(PlayerFacialExpression.Glazed), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(face);
            }
        }

        [Test]
        public void Binding_SoiledTwinIsUsedAndFallsBackToClean()
        {
            GameObject face = new GameObject("Face Surface");
            Texture2D texture = new Texture2D(512, 256);
            Shader shader = Shader.Find("Bar Promenade/PS1 Lit");
            Assert.That(shader, Is.Not.Null);
            Material material = new Material(shader);
            try
            {
                Renderer renderer = face.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                // The hero's 8x4 layout, but with soiled twins for two faces
                // only, so the fallback to the clean cell can be seen.
                Player3DFaceAtlasCell[] clean = CreateCanonicalCells();
                var cells = new Player3DFaceAtlasCell[clean.Length + 2];
                Array.Copy(clean, cells, clean.Length);
                cells[clean.Length] = new Player3DFaceAtlasCell(
                    PlayerFacialExpression.Grimace, 4, 2, true);
                cells[clean.Length + 1] = new Player3DFaceAtlasCell(
                    PlayerFacialExpression.Neutral, 4, 0, true);
                var binding = new Player3DFaceAtlasBinding(
                    renderer,
                    texture,
                    8,
                    4,
                    cells);
                Assert.That(binding.IsConfigured, Is.True);

                // The twin when the atlas has one: Grimace's sits at (4,2).
                Assert.That(
                    binding.TryGetTextureTransform(
                        PlayerFacialExpression.Grimace,
                        true,
                        out Vector4 transform),
                    Is.True);
                Assert.That(
                    transform,
                    Is.EqualTo(new Vector4(0.125f, 0.25f, 0.5f, 0.5f)));

                // The clean cell when it has none: Watchful stays at (3,0).
                Assert.That(
                    binding.TryGetTextureTransform(
                        PlayerFacialExpression.Watchful,
                        true,
                        out transform),
                    Is.True);
                Assert.That(
                    transform,
                    Is.EqualTo(new Vector4(0.125f, 0.25f, 0.375f, 0f)));

                // A clean request never picks up the twin.
                Assert.That(
                    binding.TryGetTextureTransform(
                        PlayerFacialExpression.Grimace,
                        false,
                        out transform),
                    Is.True);
                Assert.That(
                    transform,
                    Is.EqualTo(new Vector4(0.125f, 0.25f, 0f, 0.5f)));

                // The presenter carries the flag through to the renderer:
                // the soiled Grimace lands in the right half of the atlas.
                var presenter = new Player3DFaceAtlasPresenter();
                presenter.Configure(binding);
                Assert.That(
                    presenter.Apply(PlayerFacialExpression.Grimace, true),
                    Is.True);
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Vector4 shown = properties.GetVector(BaseMapTransformId);
                Assert.That(shown.z, Is.GreaterThanOrEqualTo(0.5f));
                Assert.That(
                    shown,
                    Is.EqualTo(new Vector4(0.125f, 0.25f, 0.5f, 0.5f)));

                // And drops back to the clean face for a twin it lacks.
                Assert.That(
                    presenter.Apply(PlayerFacialExpression.Watchful, true),
                    Is.True);
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetVector(BaseMapTransformId),
                    Is.EqualTo(new Vector4(0.125f, 0.25f, 0.375f, 0f)));
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(face);
            }
        }

        private static Player3DFaceAtlasCell[] CreateCanonicalCells()
        {
            return new[]
            {
                new Player3DFaceAtlasCell(
                    PlayerFacialExpression.Neutral, 0, 0),
                new Player3DFaceAtlasCell(
                    PlayerFacialExpression.HalfBlink, 1, 0),
                new Player3DFaceAtlasCell(
                    PlayerFacialExpression.ClosedBlink, 2, 0),
                new Player3DFaceAtlasCell(
                    PlayerFacialExpression.Watchful, 3, 0),
                new Player3DFaceAtlasCell(
                    PlayerFacialExpression.Tense, 0, 1),
                new Player3DFaceAtlasCell(
                    PlayerFacialExpression.Drowsy, 1, 1),
                new Player3DFaceAtlasCell(
                    PlayerFacialExpression.Glazed, 2, 1),
                new Player3DFaceAtlasCell(
                    PlayerFacialExpression.Slack, 3, 1),
                new Player3DFaceAtlasCell(
                    PlayerFacialExpression.Grimace, 0, 2)
            };
        }

        private static void AssertExpression(
            Player3DAnimationBinding binding,
            float normalizedTime,
            PlayerFacialExpression expected)
        {
            Assert.That(
                binding.TryGetFacialExpression(
                    normalizedTime,
                    out PlayerFacialExpression actual),
                Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

    }
}
