using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>One existing imported broom retained outside its pocket's static batch.</summary>
    [DisallowMultipleComponent]
    public sealed class CityCourtyardSweepingTool : MonoBehaviour
    {
        public const string BroomComponentName = "Broom_Residential_Timber";
        public const int MaximumTools = 4;
        private static readonly Vector3 BristleBase = new Vector3(-.82f, 0f, -.10f);
        private static readonly Vector3 ShaftLow = new Vector3(-.82f, .14f, -.10f);
        private static readonly Vector3 ShaftHigh = new Vector3(-.53f, 1.47f, -.07f);
        private static readonly Dictionary<string, CityCourtyardSweepingTool> Tools =
            new Dictionary<string, CityCourtyardSweepingTool>(MaximumTools, StringComparer.Ordinal);
        private bool initialized;

        public string PocketId { get; private set; }
        public Transform ToolRoot { get; private set; }
        public Transform RightGrip { get; private set; }
        public Transform LeftGrip { get; private set; }
        public Transform BristleContact { get; private set; }
        public MeshRenderer BroomRenderer { get; private set; }

        public static bool TryGet(string pocketId, out CityCourtyardSweepingTool tool)
        {
            if (pocketId != null && Tools.TryGetValue(pocketId, out tool) && tool != null && tool.isActiveAndEnabled)
                return true;
            tool = null;
            return false;
        }

        internal static CityCourtyardSweepingTool Create(Transform parent, string pocketId,
            Mesh source, Vector3 origin, Quaternion rotation, Vector3 scale, Color tint)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(pocketId)) throw new ArgumentException("Sweeping tool needs its source pocket ID.", nameof(pocketId));
            var host = new GameObject(pocketId + " Sweeping Tool");
            host.transform.SetParent(parent, false);
            host.transform.localPosition = origin;
            host.transform.localRotation = rotation;
            host.transform.localScale = scale;
            var tool = host.AddComponent<CityCourtyardSweepingTool>();
            tool.PocketId = pocketId;
            tool.ToolRoot = Child(host.transform, "Broom Contact Pivot", BristleBase);
            Transform meshRoot = Child(tool.ToolRoot, BroomComponentName, -BristleBase);
            // The provider already exposes a metre-normalized mesh. Retain
            // that exact asset: no copied mesh, UV mutation or material clone.
            meshRoot.gameObject.AddComponent<MeshFilter>().sharedMesh = source;
            MeshRenderer renderer = meshRoot.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            CityParkSurfaceAppearance.ApplyCombined(renderer, CityParkSurfaceKind.Timber, tint);
            CitySurfaceLibrary.ApplyRenderer(renderer);
            tool.BroomRenderer = renderer;
            tool.RightGrip = Child(tool.ToolRoot, "ANCHOR_BroomRightGrip", Vector3.Lerp(ShaftLow, ShaftHigh, .75f) - BristleBase);
            tool.LeftGrip = Child(tool.ToolRoot, "ANCHOR_BroomLeftGrip", Vector3.Lerp(ShaftLow, ShaftHigh, .55f) - BristleBase);
            tool.BristleContact = Child(tool.ToolRoot, "ANCHOR_BroomGroundContact", Vector3.zero);
            tool.initialized = true;
            tool.Register();
            return tool;
        }

        /// <summary>The resident's pause-aware clock owns phase; the rigid
        /// bristle head slides across its existing ground plane.</summary>
        public void ApplySweep(float phase01)
        {
            if (!initialized) return;
            float wave = Mathf.Sin(Mathf.Repeat(phase01, 1f) * Mathf.PI * 2f);
            ToolRoot.localPosition = BristleBase + new Vector3(wave * .12f, 0f, wave * .012f);
            ToolRoot.localRotation = Quaternion.Euler(0f, wave * 6f, 0f);
        }

        public void RestoreRestPose()
        {
            if (ToolRoot == null) return;
            ToolRoot.localPosition = BristleBase;
            ToolRoot.localRotation = Quaternion.identity;
        }

        private static Transform Child(Transform parent, string name, Vector3 position)
        {
            Transform child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = position;
            return child;
        }

        private void Register()
        {
            if (!initialized || !isActiveAndEnabled) return;
            if (Tools.TryGetValue(PocketId, out CityCourtyardSweepingTool other) && other != null && other != this)
                throw new InvalidOperationException("Duplicate sweeping tool for pocket: " + PocketId);
            if (!Tools.ContainsKey(PocketId) && Tools.Count >= MaximumTools)
                throw new InvalidOperationException("Courtyard sweeping tool budget exceeded.");
            Tools[PocketId] = this;
        }

        private void Unregister()
        {
            if (PocketId != null && Tools.TryGetValue(PocketId, out CityCourtyardSweepingTool current) && current == this)
                Tools.Remove(PocketId);
        }

        private void OnEnable() => Register();
        private void OnDisable() { Unregister(); RestoreRestPose(); }
        private void OnDestroy() => Unregister();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => Tools.Clear();
    }
}
