using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Rendering
{
    /// <summary>
    /// Crisp role captions after the world finish. A same-frame visible-actor
    /// mask distinguishes the actual actor surface from walls at the head bone;
    /// neither a physics collider nor a permissive head-depth bias is needed.
    /// </summary>
    internal sealed class NpcNameplatePass : ScriptableRenderPass
    {
        private const int Capacity = 32;
        private static readonly int DepthId = Shader.PropertyToID("_NpcSceneDepth");
        private static readonly int MaskId = Shader.PropertyToID("_NpcVisibleActors");
        private static readonly int ActorId = Shader.PropertyToID("_NpcActorId");
        private static readonly int AtlasId = Shader.PropertyToID("_NpcFontAtlas");
        private static readonly int SizeId = Shader.PropertyToID("_NpcTargetSize");
        private static readonly int ProbesId = Shader.PropertyToID("_NpcProbes");
        private static readonly int ExtraProbesId = Shader.PropertyToID("_NpcExtraProbes");
        private static readonly int PlanesId = Shader.PropertyToID("_NpcPlanes");
        private static readonly int OverlapsId = Shader.PropertyToID("_NpcOverlapMasks");
        private readonly List<Candidate> candidates = new List<Candidate>(Capacity);
        private readonly List<Vector3> vertices = new List<Vector3>(2048);
        private readonly List<Vector4> uv = new List<Vector4>(2048);
        private readonly List<Color32> colors = new List<Color32>(2048);
        private readonly List<int> triangles = new List<int>(3072);
        private readonly Vector4[] probes = new Vector4[Capacity];
        private readonly Vector4[] extraProbes = new Vector4[Capacity];
        private readonly Vector4[] planes = new Vector4[Capacity];
        private readonly Vector4[] overlaps = new Vector4[Capacity];
        private readonly StringBuilder characters = new StringBuilder(256);
        private readonly List<Material> rendererMaterials = new List<Material>(8);
        private Material material;
        private Mesh mesh;
        private Font font;
        private bool preparingFont;
        private bool atlasDirty;
        private float aspectFraction = 1f;
        private Matrix4x4 gpuProjection;
        private Camera currentCamera;
        private int targetWidth;
        private int targetHeight;
        private float canvasScale;

        private struct Candidate
        {
            public NpcNameplateTarget Target;
            public string Text;
            public float Distance;
            public float Opacity;
            public bool Selected;
            public Rect Panel;
            public float Baseline;
            public float TextLeft;
            public float Depth;
        }

        private sealed class PassData
        {
            public NpcNameplatePass Owner;
            public TextureHandle Depth;
            public TextureHandle Mask;
        }

        public NpcNameplatePass()
        {
            ConfigureInput(ScriptableRenderPassInput.Depth);
            requiresIntermediateTexture = true;
        }

        internal int CandidateCount => candidates.Count;
        internal Rect LastPanelRect => candidates.Count > 0 ? candidates[0].Panel : default;
        internal float LastOpacity => candidates.Count > 0 ? candidates[0].Opacity : 0f;
        internal string LastText => candidates.Count > 0 ? candidates[0].Text : string.Empty;

        internal bool TryGetLayout(NpcNameplateTarget target, out Rect rect, out float opacity)
        {
            foreach (Candidate candidate in candidates)
            {
                if (candidate.Target != target) continue;
                rect = candidate.Panel;
                opacity = candidate.Opacity;
                return true;
            }
            rect = default;
            opacity = 0f;
            return false;
        }

        public bool IsNeeded(Camera camera)
        {
            candidates.Clear();
            if (camera == null || camera.cameraType != CameraType.Game ||
                camera.TryGetComponent(out Ps1VertexJitterExclusion _) ||
                !camera.TryGetComponent(out NpcNameplateContext context) ||
                context.IsSuppressed || NpcNameplateTarget.ActiveTargets.Count == 0)
                return false;
            if (material == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/NpcNameplate");
                if (shader == null || !shader.isSupported) return false;
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            if (font == null)
            {
                font = RetroUiTheme.InterfaceFont;
                Font.textureRebuilt -= OnFontTextureRebuilt;
                Font.textureRebuilt += OnFontTextureRebuilt;
            }
            return font != null;
        }

        public void Setup(float imageWidthFraction) =>
            aspectFraction = Mathf.Clamp(imageWidthFraction, 0.01f, 1f);

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            Camera camera = cameraData.camera;
            if (material == null || !camera.TryGetComponent(out NpcNameplateContext context) ||
                context.IsSuppressed) return;
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            TextureHandle color = resources.activeColorTexture;
            TextureHandle depth = resources.cameraDepthTexture;
            if (resources.isActiveTargetBackBuffer || !color.IsValid() || !depth.IsValid()) return;
            TextureDesc descriptor = graph.GetTextureDesc(color);
            Vector2Int dimensions = descriptor.CalculateFinalDimensions();
            targetWidth = dimensions.x;
            targetHeight = dimensions.y;
            currentCamera = camera;
            // This pass requires an intermediate texture, just like the
            // world's final composite; use its GPU projection orientation.
            gpuProjection = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true);
            PrepareCandidates(context);
            if (candidates.Count == 0) return;

            descriptor.name = "NPC Visible Actor Mask";
            descriptor.colorFormat = GraphicsFormat.R8_UNorm;
            descriptor.depthBufferBits = DepthBits.None;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.bindTextureMS = false;
            descriptor.clearBuffer = true;
            descriptor.clearColor = Color.clear;
            descriptor.filterMode = FilterMode.Point;
            TextureHandle mask = graph.CreateTexture(descriptor);

            using (IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass(
                       "NPC Visible Surfaces", out PassData data))
            {
                data.Owner = this;
                data.Depth = depth;
                builder.UseTexture(depth);
                // Only visible actor fragments are written. WriteAll includes
                // Discard and makes RenderGraph skip this texture's clear;
                // pooled IDs would then survive behind a newly placed wall.
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext raster) =>
                    passData.Owner.DrawActorMask(raster, passData.Depth));
            }
            using (IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass(
                       "NPC Role Captions", out PassData data))
            {
                data.Owner = this;
                data.Depth = depth;
                data.Mask = mask;
                builder.UseTexture(depth);
                builder.UseTexture(mask);
                builder.SetRenderAttachment(color, 0, AccessFlags.ReadWrite);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext raster) =>
                    passData.Owner.DrawCaptions(raster, passData.Depth, passData.Mask));
            }
        }

        private void PrepareCandidates(NpcNameplateContext context)
        {
            candidates.Clear();
            Vector3 hero = context.Listener.transform.position;
            foreach (NpcNameplateTarget target in NpcNameplateTarget.ActiveTargets)
            {
                if (target == null || !target.IsPresent || target.IsSpeaking ||
                    target.gameObject.scene != context.Listener.gameObject.scene ||
                    (currentCamera.cullingMask & (1 << target.gameObject.layer)) == 0) continue;
                float distance = Vector3.Distance(hero, target.ActorRoot.position);
                float opacity = NpcNameplatePolicy.DistanceOpacity(distance);
                if (opacity <= 0f) continue;
                Vector3 position = currentCamera.WorldToViewportPoint(
                    target.Head.position + Vector3.up * target.HeadClearance);
                if (position.z <= currentCamera.nearClipPlane || position.x < 0f ||
                    position.x > 1f || position.y < 0f || position.y > 1f) continue;
                candidates.Add(new Candidate
                {
                    Target = target,
                    Text = LocalizationService.Get(target.LocalizationKey),
                    Distance = distance,
                    Opacity = opacity,
                    Selected = target.MatchesInteraction(context.Listener.ActiveInteractable),
                    Depth = position.z
                });
            }
            candidates.Sort(CompareCandidates);
            if (candidates.Count > Capacity) candidates.RemoveRange(Capacity, candidates.Count - Capacity);
            RequestFont();
            canvasScale = RetroUiTheme.CalculateCanvas(targetWidth, targetHeight).Scale;
            float leftEdge = targetWidth * (1f - aspectFraction) * 0.5f;
            Rect viewport = new Rect(leftEdge, 0f, targetWidth * aspectFraction, targetHeight);
            for (int index = 0; index < candidates.Count;)
            {
                Candidate candidate = candidates[index];
                MeasureText(candidate.Text, out float width, out float minY, out float maxY);
                Vector3 anchor = currentCamera.WorldToViewportPoint(
                    candidate.Target.Head.position + Vector3.up * candidate.Target.HeadClearance);
                float panelWidth = (width + 8f) * canvasScale;
                float panelHeight = (Mathf.Max(NpcNameplatePolicy.FontSize, maxY - minY) + 4f) * canvasScale;
                candidate.Panel = new Rect(Mathf.Round(anchor.x * targetWidth - panelWidth * 0.5f),
                    Mathf.Round(anchor.y * targetHeight), Mathf.Ceil(panelWidth), Mathf.Ceil(panelHeight));
                candidate.TextLeft = candidate.Panel.x + 4f * canvasScale;
                candidate.Baseline = candidate.Panel.center.y - (minY + maxY) * canvasScale * 0.5f;
                bool fits = viewport.Contains(candidate.Panel.min) && viewport.Contains(candidate.Panel.max);
                if (!fits) { candidates.RemoveAt(index); continue; }
                candidates[index] = candidate;
                index++;
            }
            BuildMesh();
        }

        private static int CompareCandidates(Candidate a, Candidate b)
        {
            int selected = b.Selected.CompareTo(a.Selected);
            if (selected != 0) return selected;
            int distance = a.Distance.CompareTo(b.Distance);
            return distance != 0 ? distance : string.CompareOrdinal(a.Target.StableId, b.Target.StableId);
        }

        private void RequestFont()
        {
            characters.Clear();
            foreach (Candidate candidate in candidates) characters.Append(candidate.Text);
            preparingFont = true;
            try { font.RequestCharactersInTexture(characters.ToString(), NpcNameplatePolicy.FontSize, FontStyle.Normal); }
            finally { preparingFont = false; }
        }

        private void OnFontTextureRebuilt(Font rebuilt)
        {
            if (rebuilt == font) atlasDirty = true;
        }

        private void MeasureText(string text, out float width, out float minY, out float maxY)
        {
            width = 0f;
            minY = float.MaxValue;
            maxY = float.MinValue;
            foreach (char letter in text)
            {
                if (!font.GetCharacterInfo(letter, out CharacterInfo glyph, NpcNameplatePolicy.FontSize)) continue;
                width += glyph.advance;
                minY = Mathf.Min(minY, glyph.minY);
                maxY = Mathf.Max(maxY, glyph.maxY);
            }
            if (minY == float.MaxValue) { minY = 0f; maxY = NpcNameplatePolicy.FontSize; }
        }

        private void BuildMesh()
        {
            vertices.Clear(); uv.Clear(); colors.Clear(); triangles.Clear();
            for (int index = 0; index < candidates.Count; index++)
            {
                Candidate candidate = candidates[index];
                Vector3 head = candidate.Target.Head.position;
                Vector2 headUv = TextureUv(head);
                Vector2 bodyUv = TextureUv(head - Vector3.up * 0.22f);
                Vector2 leftUv = TextureUv(head - currentCamera.transform.right * 0.06f);
                Vector2 rightUv = TextureUv(head + currentCamera.transform.right * 0.06f);
                probes[index] = new Vector4(headUv.x, headUv.y, bodyUv.x, bodyUv.y);
                extraProbes[index] = new Vector4(leftUv.x, leftUv.y, rightUv.x, rightUv.y);
                Vector3 panelCenter = currentCamera.ViewportToWorldPoint(new Vector3(
                    candidate.Panel.center.x / targetWidth, candidate.Panel.center.y / targetHeight, candidate.Depth));
                Vector2 panelUv = TextureUv(panelCenter);
                planes[index] = new Vector4(candidate.Depth, candidate.Opacity, panelUv.x, panelUv.y);
                uint overlapMask = 0;
                for (int previous = 0; previous < index; previous++)
                {
                    Rect occupied = candidates[previous].Panel;
                    occupied.xMin -= 2f * canvasScale;
                    occupied.xMax += 2f * canvasScale;
                    occupied.yMin -= 2f * canvasScale;
                    occupied.yMax += 2f * canvasScale;
                    if (occupied.Overlaps(candidate.Panel)) overlapMask |= 1u << previous;
                }
                // Two exact 16-bit pieces survive float uniform storage.
                overlaps[index] = new Vector4(overlapMask & 65535u, overlapMask >> 16, 0f, 0f);
                AddQuad(candidate.Panel, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero,
                    RetroUiTheme.WithAlpha(RetroUiTheme.Ink, 0.76f), index, true, candidate.Depth);
                float cursor = candidate.TextLeft;
                foreach (char letter in candidate.Text)
                {
                    if (!font.GetCharacterInfo(letter, out CharacterInfo glyph, NpcNameplatePolicy.FontSize)) continue;
                    if (glyph.glyphWidth > 0 && glyph.glyphHeight > 0)
                        AddQuad(new Rect(cursor + glyph.minX * canvasScale,
                            candidate.Baseline + glyph.minY * canvasScale,
                            glyph.glyphWidth * canvasScale, glyph.glyphHeight * canvasScale),
                            glyph.uvBottomLeft, glyph.uvBottomRight, glyph.uvTopRight, glyph.uvTopLeft,
                            RetroUiTheme.Text, index, false, candidate.Depth);
                    cursor += glyph.advance * canvasScale;
                }
            }
            if (mesh == null)
            {
                mesh = new Mesh { name = "NPC Role Caption Overlay", hideFlags = HideFlags.HideAndDontSave };
                mesh.MarkDynamic();
            }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            atlasDirty = false;
        }

        private void AddQuad(Rect rect, Vector2 bottomLeft, Vector2 bottomRight,
            Vector2 topRight, Vector2 topLeft, Color color, int index, bool backdrop, float eyeDepth)
        {
            int start = vertices.Count;
            AddVertex(rect.xMin, rect.yMin, bottomLeft, color, index, backdrop, eyeDepth);
            AddVertex(rect.xMax, rect.yMin, bottomRight, color, index, backdrop, eyeDepth);
            AddVertex(rect.xMax, rect.yMax, topRight, color, index, backdrop, eyeDepth);
            AddVertex(rect.xMin, rect.yMax, topLeft, color, index, backdrop, eyeDepth);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        }

        private void AddVertex(float x, float y, Vector2 atlasUv, Color color, int index,
            bool backdrop, float eyeDepth)
        {
            Vector3 world = currentCamera.ViewportToWorldPoint(new Vector3(x / targetWidth, y / targetHeight, eyeDepth));
            Vector4 clip = gpuProjection * currentCamera.worldToCameraMatrix * new Vector4(world.x, world.y, world.z, 1f);
            vertices.Add(new Vector3(clip.x / clip.w, clip.y / clip.w, 0f));
            uv.Add(new Vector4(atlasUv.x, atlasUv.y, backdrop ? 1f : 0f, index));
            colors.Add(color);
        }

        private Vector2 TextureUv(Vector3 world)
        {
            Vector4 clip = gpuProjection * currentCamera.worldToCameraMatrix * new Vector4(world.x, world.y, world.z, 1f);
            Vector2 result = new Vector2(clip.x / clip.w, clip.y / clip.w) * 0.5f + Vector2.one * 0.5f;
            if (SystemInfo.graphicsUVStartsAtTop) result.y = 1f - result.y;
            return result;
        }

        private void DrawActorMask(RasterGraphContext context, TextureHandle depth)
        {
            material.SetTexture(DepthId, depth);
            material.SetVector(SizeId, new Vector4(targetWidth, targetHeight, 1f / targetWidth, 1f / targetHeight));
            for (int index = 0; index < candidates.Count; index++)
            {
                context.cmd.SetGlobalFloat(ActorId, (index + 1f) / 255f);
                foreach (Renderer renderer in candidates[index].Target.VisualRenderers)
                {
                    if (renderer == null || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy ||
                        (currentCamera.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
                    renderer.GetSharedMaterials(rendererMaterials);
                    for (int submesh = 0; submesh < rendererMaterials.Count; submesh++)
                    {
                        Material original = rendererMaterials[submesh];
                        if (original == null || original.renderQueue > (int)RenderQueue.GeometryLast) continue;
                        context.cmd.DrawRenderer(renderer, material, submesh, 0);
                    }
                }
            }
            context.cmd.SetGlobalFloat(ActorId, 0f);
        }

        private void DrawCaptions(RasterGraphContext context, TextureHandle depth, TextureHandle mask)
        {
            if (atlasDirty && !preparingFont) BuildMesh();
            material.SetTexture(DepthId, depth);
            material.SetTexture(MaskId, mask);
            material.SetTexture(AtlasId, font.material.mainTexture);
            material.SetVectorArray(ProbesId, probes);
            material.SetVectorArray(ExtraProbesId, extraProbes);
            material.SetVectorArray(PlanesId, planes);
            material.SetVectorArray(OverlapsId, overlaps);
            context.cmd.DrawMesh(mesh, Matrix4x4.identity, material, 0, 1);
        }

        public void Dispose()
        {
            Font.textureRebuilt -= OnFontTextureRebuilt;
            CoreUtils.Destroy(material);
            CoreUtils.Destroy(mesh);
            material = null; mesh = null; font = null;
            candidates.Clear();
        }
    }
}
