using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The imported watch and temporary deformation of the hero's existing left sleeve.</summary>
    internal sealed class PlayerWatchPresentation : IDisposable
    {
        private const string ResourceFolder = "Player/Watch/";
        private static readonly int SegmentsId = Shader.PropertyToID("_Segments");
        private static readonly int ColonVisibleId = Shader.PropertyToID("_ColonVisible");
        private static readonly int[] DigitMasks = { 63, 6, 91, 79, 102, 109, 125, 7, 127, 111 };

        private readonly Player3DCharacterPresentation visual;
        private readonly GameObject model;
        private readonly Renderer[] renderers;
        private readonly Renderer lcd, skin;
        private readonly CuffSurface sleeve, cuffTrim, exposedForearm;
        private readonly MaterialPropertyBlock displayBlock = new();
        private bool skinWasVisible;
        private bool colonVisible;

        internal Transform Dial { get; }
        internal int DisplayedMinute { get; private set; } = -1;
        internal float CuffAmount { get; private set; }
        internal bool CanPullCuff => sleeve.CanBorrow && cuffTrim.CanBorrow;

        internal static PlayerWatchPresentation Create(Player3DCharacterPresentation visual,
            Transform forearm, Transform wrist, Vector3 dorsal, float travel)
        {
            var asset = Resources.Load<GameObject>(ResourceFolder + "PlayerWatch");
            if (asset == null || !visual.Registry.TryGetPart(Player3DAnatomicalPart.LeftForearm, out var skin))
                return null;
            SkinnedMeshRenderer sleeveMesh = null, cuffMesh = null;
            foreach (var binding in visual.Registry.MeshBindings)
            {
                if (binding.MeshName == "CLO_JacketForearm.L")
                    sleeveMesh = binding.Renderer as SkinnedMeshRenderer;
                else if (binding.MeshName == "CLO_JacketCuff.L")
                    cuffMesh = binding.Renderer as SkinnedMeshRenderer;
            }
            var skinMesh = skin.Renderer as SkinnedMeshRenderer;
            if (!TryBuildPullShape(sleeveMesh, forearm, wrist, travel, skinOnly: false, out Mesh sleeveShape))
                return null;
            if (!TryBuildPullShape(cuffMesh, forearm, wrist, travel, skinOnly: false, out Mesh trimShape))
            {
                UnityEngine.Object.Destroy(sleeveShape);
                return null;
            }
            if (!TryBuildPullShape(skinMesh, forearm, wrist, travel, skinOnly: true, out Mesh skinShape))
            {
                UnityEngine.Object.Destroy(sleeveShape);
                UnityEngine.Object.Destroy(trimShape);
                return null;
            }

            var model = new GameObject("Hero Wristwatch");
            UnityEngine.Object.Instantiate(asset, model.transform, false);
            Transform dial = null;
            foreach (Transform part in model.GetComponentsInChildren<Transform>())
                if (part.name == "Dial") { dial = part; break; }
            if (dial == null)
            {
                UnityEngine.Object.Destroy(model);
                UnityEngine.Object.Destroy(sleeveShape);
                UnityEngine.Object.Destroy(trimShape);
                UnityEngine.Object.Destroy(skinShape);
                Debug.LogError("The imported wristwatch is missing its Dial anchor.");
                return null;
            }
            Vector3 along = (wrist.position - forearm.position).normalized;
            Vector3 normal = Vector3.ProjectOnPlane(dorsal, along).normalized;
            model.transform.SetPositionAndRotation(wrist.position - along * .033f,
                Quaternion.LookRotation(normal, Vector3.Cross(along, normal)));
            // Imported empties have the source basis. The mesh and anchor
            // keep their authored position while the dial uses the metric mount basis.
            dial.rotation = model.transform.rotation;
            model.transform.SetParent(forearm, true);
            return new PlayerWatchPresentation(visual, model, dial, skin.Renderer,
                new CuffSurface(sleeveMesh, sleeveShape), new CuffSurface(cuffMesh, trimShape),
                new CuffSurface(skinMesh, skinShape));
        }

        private PlayerWatchPresentation(Player3DCharacterPresentation visual, GameObject model, Transform dial,
            Renderer skin, CuffSurface sleeve, CuffSurface cuffTrim, CuffSurface exposedForearm)
        {
            this.visual = visual;
            this.model = model;
            this.sleeve = sleeve;
            this.skin = skin;
            this.cuffTrim = cuffTrim;
            this.exposedForearm = exposedForearm;
            Dial = dial;
            renderers = model.GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in renderers)
            {
                string finish = renderer.name.Substring(renderer.name.LastIndexOf('_') + 1);
                renderer.sharedMaterial = Resources.Load<Material>(ResourceFolder + finish);
                if (finish == "LCD") lcd = renderer;
            }
            visual.RegisterAccessoryRenderers(renderers);
            RefreshTime();
        }

        private static bool TryBuildPullShape(SkinnedMeshRenderer renderer, Transform forearm,
            Transform wrist, float travel, bool skinOnly, out Mesh result)
        {
            result = null;
            if (renderer == null) return false;
            Mesh source = renderer.sharedMesh;
            if (source == null || !source.isReadable) return false;
            int index = Array.IndexOf(renderer.bones, forearm);
            if (index < 0) return false;
            Matrix4x4 inBindMesh = source.bindposes[index].inverse;
            Vector3 elbow = inBindMesh.MultiplyPoint3x4(Vector3.zero);
            Vector3 end = inBindMesh.MultiplyPoint3x4(forearm.InverseTransformPoint(wrist.position));
            Vector3 axis = (end - elbow).normalized;
            float length = Vector3.Distance(elbow, end);
            if (length <= .0001f) return false;

            Vector3[] points = source.vertices;
            var offsets = new Vector3[points.Length];
            float localTravel = travel * length / Vector3.Distance(wrist.position, forearm.position);
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 local = points[i] - elbow;
                float along = Vector3.Dot(local, axis);
                if (skinOnly)
                {
                    // Keep the covered skin below the sleeve folds at the elbow.
                    offsets[i] = axis * Mathf.Max(0f, length * .64f - along);
                    continue;
                }
                float weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.37f, .98f, along / length));
                // Compress the distal rings toward the pinned elbow and flare their hem.
                Vector3 radial = local - along * axis;
                offsets[i] = weight * (radial * .16f - axis * localTravel);
            }
            result = UnityEngine.Object.Instantiate(source);
            result.name = source.name + " Raised Watch Cuff";
            result.hideFlags = HideFlags.HideAndDontSave;
            result.AddBlendShapeFrame("WatchCuffPull", 100f, offsets, null, null);
            return true;
        }

        internal void PullCuff(float amount)
        {
            if (skin == null || amount == CuffAmount) return;
            if (CuffAmount == 0f)
            {
                skinWasVisible = skin.enabled;
                exposedForearm.SetPull(1f);
                skin.enabled = true;
            }

            sleeve.SetPull(amount);
            cuffTrim.SetPull(amount);
            if (amount == 0f)
            {
                skin.enabled = skinWasVisible;
                exposedForearm.SetPull(0f);
            }
            CuffAmount = amount;
        }

        internal void SetWorn(bool worn)
        {
            if (model == null) return;
            if (model.activeSelf != worn) model.SetActive(worn);
            if (worn) RefreshTime();
        }

        internal void RefreshTime()
        {
            if (lcd == null) return;
            int minute = GameSessionState.GameMinuteOfDay;
            bool showColon = ((long)Time.unscaledTimeAsDouble & 1L) == 0;
            if (minute == DisplayedMinute && showColon == colonVisible) return;

            if (minute != DisplayedMinute)
            {
                DisplayedMinute = minute;
                int hours = minute / 60, minutes = minute % 60;
                displayBlock.SetVector(SegmentsId, new Vector4(DigitMasks[hours / 10], DigitMasks[hours % 10],
                    DigitMasks[minutes / 10], DigitMasks[minutes % 10]));
            }
            colonVisible = showColon;
            displayBlock.SetFloat(ColonVisibleId, showColon ? 1f : 0f);
            lcd.SetPropertyBlock(displayBlock);
        }

        public void Dispose()
        {
            PullCuff(0f);
            if (visual != null) visual.UnregisterAccessoryRenderers(renderers);
            UnityEngine.Object.Destroy(model);
            sleeve.Dispose();
            cuffTrim.Dispose();
            exposedForearm.Dispose();
        }

        private sealed class CuffSurface : IDisposable
        {
            private readonly SkinnedMeshRenderer renderer;
            private readonly Mesh source, pulled;
            private readonly float[] authoredWeights;

            public CuffSurface(SkinnedMeshRenderer renderer, Mesh pulled)
            {
                this.renderer = renderer;
                this.pulled = pulled;
                source = renderer.sharedMesh;
                authoredWeights = new float[source.blendShapeCount];
            }

            public bool CanBorrow => renderer != null && renderer.enabled && renderer.sharedMesh == source;

            public void SetPull(float amount)
            {
                if (renderer == null) return;
                if (amount == 0f && renderer.sharedMesh != pulled) return;
                Mesh target = amount > 0f ? pulled : source;
                if (renderer.sharedMesh != target)
                {
                    for (int i = 0; i < authoredWeights.Length; i++)
                        authoredWeights[i] = renderer.GetBlendShapeWeight(i);
                    renderer.sharedMesh = target;
                    for (int i = 0; i < authoredWeights.Length; i++)
                        renderer.SetBlendShapeWeight(i, authoredWeights[i]);
                }
                if (target == pulled) renderer.SetBlendShapeWeight(authoredWeights.Length, amount * 100f);
            }

            public void Dispose()
            {
                SetPull(0f);
                UnityEngine.Object.Destroy(pulled);
            }
        }
    }
}
