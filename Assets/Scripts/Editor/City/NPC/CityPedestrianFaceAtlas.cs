using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Full-colour facial pixels remain separate from clothing's multiply atlas.</summary>
    internal static class CityPedestrianFaceAtlas
    {
        internal const string FishermanPath = "Assets/Pedestrians/Textures/LakeFishermanFaceAtlas.png";

        internal static void Configure(CityPedestrianAssetRegistry registry, Source source)
        {
            if (source == null) return;
            AssetDatabase.ImportAsset(source.texture_asset,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            registry.ConfigureFaceAtlas(CreateBinding(registry, source));
            Transform socket = CityPedestrianHandProps.FindSocket(registry.ModelRoot,
                CityPedestrianHandProps.MouthSocketName);
            if (socket == null) throw new InvalidOperationException("The fisherman needs his canonical mouth socket.");
            var attachment = new GameObject(SeacoastFishermanFactory.PipeMountAnchorName).transform;
            attachment.SetParent(registry.HeadAnchor, false);
            // Measured by the face authoring tool in the model's upright bind pose.
            // A child preserves the shared Avatar and every existing animation curve.
            attachment.SetPositionAndRotation(PipeMountPosition(registry, socket, source), socket.rotation);
            attachment.localScale = Vector3.one * source.pipe_scale;
        }

        internal static void Validate(CityPedestrianAssetRegistry registry, Source source)
        {
            if (source == null)
            {
                if (registry.FaceAtlas != null)
                    throw new InvalidOperationException("An undeclared pedestrian face atlas was bound.");
                return;
            }
            Player3DFaceAtlasBinding expected = CreateBinding(registry, source);
            Player3DFaceAtlasBinding actual = registry.FaceAtlas;
            if (actual == null || !actual.IsConfigured || actual.Renderer != expected.Renderer ||
                actual.Texture != expected.Texture || actual.Columns != expected.Columns || actual.Rows != expected.Rows ||
                actual.Cells.Count != expected.Cells.Count)
                throw new InvalidOperationException("Pedestrian face binding differs from its source manifest.");
            foreach (Player3DFaceAtlasCell cell in expected.Cells)
            {
                expected.TryGetTextureTransform(cell.Expression, out Vector4 transform);
                if (!actual.TryGetTextureTransform(cell.Expression, out Vector4 stored) || stored != transform)
                    throw new InvalidOperationException("Pedestrian face expression cell differs from its source.");
            }
            Transform socket = CityPedestrianHandProps.FindSocket(registry.ModelRoot,
                CityPedestrianHandProps.MouthSocketName);
            Transform attachment = CityPedestrianHandProps.FindSocket(registry.ModelRoot,
                SeacoastFishermanFactory.PipeMountAnchorName);
            Transform exhale = CityPedestrianHandProps.FindSocket(registry.ModelRoot,
                SeacoastFishermanFactory.ExhaleAnchorName);
            if (socket == null || attachment == null || attachment.parent != registry.HeadAnchor ||
                Vector3.Distance(attachment.position, PipeMountPosition(registry, socket, source)) > .001f ||
                Quaternion.Angle(attachment.rotation, socket.rotation) > .01f ||
                Vector3.Distance(attachment.localScale, Vector3.one * source.pipe_scale) > .0001f ||
                exhale == null || !exhale.IsChildOf(registry.HeadAnchor) ||
                Vector3.Dot(exhale.up, registry.transform.forward) < .99f)
                throw new InvalidOperationException("The pipe and exhale anchor must follow the painted mouth.");
        }

        private static Vector3 PipeMountPosition(CityPedestrianAssetRegistry registry, Transform socket, Source source)
        {
            // The manifest uses Blender's bind frame. The imported Model's
            // 180-degree facing correction maps it to registry (-X, Z, -Y).
            float[] offset = source.pipe_mount_offset_m;
            return socket.position + registry.transform.TransformVector(new Vector3(-offset[0], offset[2], -offset[1]));
        }

        private static Player3DFaceAtlasBinding CreateBinding(CityPedestrianAssetRegistry registry, Source source)
        {
            if (source.texture_asset != FishermanPath || source.renderer != "GEO_FaceSurface" ||
                source.columns != 4 || source.rows != 4 || source.cell_size_px != 64 ||
                source.width_px != 256 || source.height_px != 256 || source.color_space != "sRGB" ||
                source.filter_mode != "Point" || source.wrap_mode != "Clamp" || source.mipmaps ||
                source.compression != "Uncompressed" || source.uv_channel != 0 || source.uv_origin != "bottom_left" ||
                source.material_tint_hex != "FFFFFF" || source.uv_contract != "local_0_1_runtime_cell_scale_offset" ||
                source.cells == null || source.cells.Length != 5 || !File.Exists(source.texture_asset) ||
                !float.IsFinite(source.pipe_scale) || source.pipe_scale < .4f || source.pipe_scale > 1f ||
                source.pipe_mount_offset_m == null || source.pipe_mount_offset_m.Length != 3 ||
                source.pipe_mount_offset_m.Any(value => !float.IsFinite(value) || Mathf.Abs(value) > .15f) ||
                source.mouth_position_m == null || source.mouth_position_m.Length != 3 ||
                source.mouth_position_m.Any(value => !float.IsFinite(value)))
                throw new InvalidOperationException("Pedestrian PNG face contract is invalid.");

            using (SHA256 sha = SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(source.texture_asset)))
                    .Replace("-", string.Empty);
                if (!string.Equals(actual, source.sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Pedestrian face PNG differs from its manifest hash.");
            }
            var importer = AssetImporter.GetAtPath(source.texture_asset) as TextureImporter;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(source.texture_asset);
            if (texture == null || texture.width != 256 || texture.height != 256 || importer == null ||
                importer.filterMode != FilterMode.Point || importer.wrapMode != TextureWrapMode.Clamp ||
                importer.mipmapEnabled || !importer.sRGBTexture ||
                importer.textureCompression != TextureImporterCompression.Uncompressed)
                throw new InvalidOperationException("Pedestrian face PNG needs Point/Clamp/sRGB with no compression or mipmaps.");

            CityPedestrianRendererBinding surface = registry.RendererBindings.SingleOrDefault(
                candidate => candidate.RendererName == source.renderer);
            if (surface == null || surface.UsesDetailAtlas || !(surface.Renderer is SkinnedMeshRenderer renderer))
                throw new InvalidOperationException("Pedestrian face must be its own skinned surface, outside the detail atlas.");
            for (int variant = 0; variant < 4; variant++)
                if (surface.GetColor(variant) != Color.white)
                    throw new InvalidOperationException("Pedestrian face pixels must retain white tint in every palette.");
            Mesh mesh = renderer.sharedMesh;
            Vector2[] uv = mesh.uv;
            if (uv.Length != mesh.vertexCount || uv.Any(value => !float.IsFinite(value.x) || !float.IsFinite(value.y) ||
                value.x < -.001f || value.x > 1.001f || value.y < -.001f || value.y > 1.001f) ||
                uv.Max(value => value.x) - uv.Min(value => value.x) < .9f ||
                uv.Max(value => value.y) - uv.Min(value => value.y) < .9f)
                throw new InvalidOperationException("Pedestrian face must expose local 0..1 UVs for runtime expressions.");
            if (registry.Renderers.Any(candidate => candidate.name == "ACC_Eye.L" ||
                candidate.name == "ACC_Eye.R" || candidate.name == "ACC_Nose"))
                throw new InvalidOperationException("The PNG face cannot retain separate geometric eyes or nose.");

            Player3DFaceAtlasCell[] cells = source.cells.Select(cell =>
                new Player3DFaceAtlasCell(Enum.Parse<PlayerFacialExpression>(cell.expression), cell.column, cell.row)).ToArray();
            var binding = new Player3DFaceAtlasBinding(renderer, texture, source.columns, source.rows, cells);
            if (!binding.IsConfigured || cells.Select(cell => cell.Expression).Distinct().Count() != 5)
                throw new InvalidOperationException("Pedestrian face must contain the five canonical expressions.");
            return binding;
        }

        [Serializable]
        internal sealed class Source
        {
            public string texture_asset, renderer, sha256, color_space;
            public string filter_mode, wrap_mode, compression, uv_origin;
            public string material_tint_hex, uv_contract;
            public int columns, rows, cell_size_px, width_px, height_px, uv_channel;
            public bool mipmaps;
            public float pipe_scale;
            public float[] mouth_position_m, pipe_mount_offset_m;
            public Cell[] cells;
        }

        [Serializable]
        internal sealed class Cell
        {
            public string expression;
            public int column, row;
        }
    }
}
