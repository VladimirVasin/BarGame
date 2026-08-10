using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class RuntimePrimitiveFactoryTests
    {
        [Test]
        public void DefaultPrimitivesUseSharedSupportedUrpLitMaterial()
        {
            GameObject box = RuntimePrimitiveFactory.CreateBox(
                "Box",
                null,
                Vector3.zero,
                Vector3.one,
                Color.red,
                false);
            GameObject cylinder =
                RuntimePrimitiveFactory.CreateCylinder(
                    "Cylinder",
                    null,
                    Vector3.zero,
                    Vector3.one,
                    Color.blue,
                    false);

            try
            {
                Material expected = Resources.Load<Material>(
                    RuntimePrimitiveFactory
                        .DefaultMaterialResourcePath);
                Material boxMaterial =
                    box.GetComponent<Renderer>().sharedMaterial;
                Material cylinderMaterial =
                    cylinder.GetComponent<Renderer>().sharedMaterial;

                Assert.That(expected, Is.Not.Null);
                Assert.That(boxMaterial, Is.SameAs(expected));
                Assert.That(cylinderMaterial, Is.SameAs(expected));
                Assert.That(
                    expected.shader.name,
                    Is.EqualTo("Universal Render Pipeline/Lit"));
                Assert.That(expected.shader.isSupported, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(box);
                Object.DestroyImmediate(cylinder);
            }
        }

        [Test]
        public void CylindersReuseEightSidedPresentationMesh()
        {
            GameObject first = RuntimePrimitiveFactory.CreateCylinder(
                "First",
                null,
                Vector3.zero,
                Vector3.one,
                Color.white,
                false);
            GameObject second = RuntimePrimitiveFactory.CreateCylinder(
                "Second",
                null,
                Vector3.zero,
                Vector3.one,
                Color.white,
                false);

            try
            {
                Mesh firstMesh = first.GetComponent<MeshFilter>().sharedMesh;
                Mesh secondMesh = second.GetComponent<MeshFilter>().sharedMesh;

                Assert.That(firstMesh, Is.SameAs(secondMesh));
                Assert.That(firstMesh.vertexCount, Is.EqualTo(50));
                Assert.That(firstMesh.triangles.Length / 3, Is.EqualTo(32));
                Assert.That(
                    firstMesh.name,
                    Is.EqualTo("Shared PS1 Eight-Sided Cylinder"));
                Assert.That(first.GetComponent<Collider>(), Is.Null);
                Assert.That(second.GetComponent<Collider>(), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void CombinedBoxesUseOneColliderFreeRenderer()
        {
            Bounds[] boxes =
            {
                new Bounds(
                    new Vector3(-1.5f, 0.5f, 0f),
                    new Vector3(2f, 1f, 0.25f)),
                new Bounds(
                    new Vector3(1.5f, 0.75f, 0f),
                    new Vector3(1f, 1.5f, 0.5f))
            };
            GameObject combined =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    "Combined",
                    null,
                    boxes,
                    Color.yellow);

            try
            {
                Mesh mesh =
                    combined.GetComponent<MeshFilter>().sharedMesh;

                Assert.That(mesh, Is.Not.Null);
                Assert.That(mesh.vertexCount, Is.EqualTo(48));
                Assert.That(
                    mesh.triangles.Length / 3,
                    Is.EqualTo(24));
                Assert.That(
                    combined.GetComponentsInChildren<Renderer>(true),
                    Has.Length.EqualTo(1));
                Assert.That(
                    combined.GetComponentsInChildren<Collider>(true),
                    Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(combined);
            }
        }

        [Test]
        public void CombinedBoxesCanShareTheirMeshWithAStaticCollider()
        {
            Bounds[] boxes =
            {
                new Bounds(
                    new Vector3(-1f, 0.08f, 0f),
                    new Vector3(2f, 0.16f, 1f)),
                new Bounds(
                    new Vector3(1f, 0.08f, 0f),
                    new Vector3(2f, 0.16f, 1f))
            };
            GameObject combined =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    "Walkable Combined",
                    null,
                    boxes,
                    Color.gray,
                    true);

            try
            {
                Mesh renderMesh =
                    combined.GetComponent<MeshFilter>().sharedMesh;
                MeshCollider meshCollider =
                    combined.GetComponent<MeshCollider>();

                Assert.That(meshCollider, Is.Not.Null);
                Assert.That(
                    combined.GetComponents<Collider>(),
                    Has.Length.EqualTo(1));
                Assert.That(
                    meshCollider.sharedMesh,
                    Is.SameAs(renderMesh));
                Assert.That(meshCollider.convex, Is.False);
                Assert.That(
                    meshCollider.bounds.max.y,
                    Is.EqualTo(0.16f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(combined);
            }
        }

        [Test]
        public void CombinedBoxesCanUseXZPlanarUvsWithoutChangingColliderMesh()
        {
            const float tileSize = 12f;
            Bounds[] boxes =
            {
                new Bounds(
                    new Vector3(-9f, 0.08f, 6f),
                    new Vector3(24f, 0.16f, 6f)),
                new Bounds(
                    new Vector3(15f, 0.08f, -6f),
                    new Vector3(6f, 0.16f, 24f))
            };
            GameObject combined =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    "Planar Road",
                    null,
                    boxes,
                    Color.gray,
                    true,
                    tileSize);

            try
            {
                Mesh mesh =
                    combined.GetComponent<MeshFilter>().sharedMesh;
                MeshCollider meshCollider =
                    combined.GetComponent<MeshCollider>();
                Vector3[] vertices = mesh.vertices;
                Vector2[] uvs = mesh.uv;

                Assert.That(meshCollider, Is.Not.Null);
                Assert.That(meshCollider.sharedMesh, Is.SameAs(mesh));
                Assert.That(uvs, Has.Length.EqualTo(vertices.Length));
                for (int index = 0; index < vertices.Length; index++)
                {
                    Assert.That(
                        uvs[index].x,
                        Is.EqualTo(vertices[index].x / tileSize)
                            .Within(0.0001f));
                    Assert.That(
                        uvs[index].y,
                        Is.EqualTo(vertices[index].z / tileSize)
                            .Within(0.0001f));
                }
            }
            finally
            {
                Object.DestroyImmediate(combined);
            }
        }

        [Test]
        public void RoadSurfaceUsesPackagedTextureAndSharedMaterialProperties()
        {
            Texture2D texture = Resources.Load<Texture2D>(
                CityExteriorAppearance.RoadTextureResourcePath);

            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.width, Is.EqualTo(512));
            Assert.That(texture.height, Is.EqualTo(512));
            Assert.That(texture.isReadable, Is.False);

            string assetPath = AssetDatabase.GetAssetPath(texture);
            TextureImporter importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.textureType,
                Is.EqualTo(TextureImporterType.Default));
            Assert.That(importer.sRGBTexture, Is.True);
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(importer.anisoLevel, Is.EqualTo(4));
            Assert.That(importer.maxTextureSize, Is.EqualTo(512));
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.isReadable, Is.False);
            AssertRoadTextureSourceIsOpaqueAndTileable(assetPath);

            Bounds[] boxes =
            {
                new Bounds(
                    new Vector3(0f, 0.08f, 0f),
                    new Vector3(12f, 0.16f, 6f))
            };
            GameObject road =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    "Road Surface",
                    null,
                    boxes,
                    CityExteriorAppearance.Asphalt,
                    true,
                    CityExteriorAppearance.RoadTextureTileSize);

            try
            {
                Renderer renderer = road.GetComponent<Renderer>();
                int preservedId =
                    Shader.PropertyToID("_RoadSurfacePreservedTest");
                var properties = new MaterialPropertyBlock();
                properties.SetFloat(preservedId, 0.42f);
                renderer.SetPropertyBlock(properties);

                CityExteriorAppearance.ApplyRoadSurface(renderer);

                properties.Clear();
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial));
                Assert.That(
                    properties.GetTexture(
                        Shader.PropertyToID("_BaseMap")),
                    Is.SameAs(texture));
                Assert.That(
                    properties.GetColor(
                        Shader.PropertyToID("_BaseColor")),
                    Is.EqualTo(Color.white));
                Assert.That(
                    properties.GetColor(
                        Shader.PropertyToID("_Color")),
                    Is.EqualTo(Color.white));
                Assert.That(
                    properties.GetFloat(
                        Shader.PropertyToID("_Smoothness")),
                    Is.EqualTo(CityExteriorAppearance.RoadSmoothness)
                        .Within(0.0001f));
                Assert.That(
                    properties.GetFloat(
                        Shader.PropertyToID("_Metallic")),
                    Is.EqualTo(0f).Within(0.0001f));
                Assert.That(
                    properties.GetFloat(preservedId),
                    Is.EqualTo(0.42f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(road);
            }
        }

        private static void AssertRoadTextureSourceIsOpaqueAndTileable(
            string assetPath)
        {
            byte[] pngBytes = File.ReadAllBytes(
                Path.GetFullPath(assetPath));
            Assert.That(pngBytes, Has.Length.GreaterThan(25));
            Assert.That(
                pngBytes[25],
                Is.EqualTo(2),
                "Road albedo must use opaque RGB PNG storage.");

            var source = new Texture2D(
                2,
                2,
                TextureFormat.RGB24,
                false);
            try
            {
                Assert.That(
                    ImageConversion.LoadImage(source, pngBytes, false),
                    Is.True);
                Assert.That(source.width, Is.EqualTo(source.height));
                Assert.That(source.width, Is.GreaterThanOrEqualTo(512));

                Color32[] pixels = source.GetPixels32();
                long edgeDelta = 0L;
                for (int y = 0; y < source.height; y++)
                {
                    Color32 left = pixels[y * source.width];
                    Color32 right = pixels[
                        y * source.width + source.width - 1];
                    edgeDelta += ChannelDelta(left, right);
                }

                for (int x = 0; x < source.width; x++)
                {
                    Color32 top = pixels[x];
                    Color32 bottom = pixels[
                        (source.height - 1) * source.width + x];
                    edgeDelta += ChannelDelta(top, bottom);
                }

                double meanChannelDelta =
                    edgeDelta /
                    ((source.width + source.height) * 3.0);
                Assert.That(
                    meanChannelDelta,
                    Is.LessThanOrEqualTo(16.0),
                    "Road albedo edges diverge too much for Repeat sampling.");
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        private static int ChannelDelta(Color32 first, Color32 second)
        {
            return Mathf.Abs(first.r - second.r) +
                   Mathf.Abs(first.g - second.g) +
                   Mathf.Abs(first.b - second.b);
        }
    }
}
