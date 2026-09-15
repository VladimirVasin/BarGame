using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Four quiet sellers; one shared idle pose with occasional counter work.</summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class CityFairVendors : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColor = Shader.PropertyToID("_Color");
        private static readonly float[] Heights = { 1.02f, .96f, .99f, 1.04f };
        private static readonly Color[] CoatTints =
        {
            new Color(.83f, .84f, .95f), new Color(1.18f, .86f, .78f),
            new Color(.91f, 1.05f, .91f), new Color(1.07f, .96f, .81f)
        };
        private static readonly Color[] KnitTints =
        {
            new Color(.72f, .72f, .80f), new Color(.89f, .85f, .77f),
            new Color(.82f, .75f, .73f), new Color(.71f, .78f, .82f)
        };
        private CityFairPlan plan;
        private Transform[] spines;
        private Renderer[][] clothing;
        private Color[][] colors;
        private MaterialPropertyBlock block;
        private bool initialized;

        public VillageResidentPresentation[] Actors { get; private set; } =
            Array.Empty<VillageResidentPresentation>();
        public int ActorCount => Actors.Length;
        public float ElapsedSeconds { get; private set; }
        public bool HandsMatch { get; private set; } = true;

        public static CityFairVendors Build(Transform parent, CityFairPlan fair)
        {
            if (fair == null) throw new ArgumentNullException(nameof(fair));
            if (!fair.IsEnabled) return null;
            var library = VillageResidentLibrary.Load();
            if (library == null || library.GetPrefab(VillageResidentRole.StationWorker) == null)
                throw new InvalidOperationException("The fair requires the authored ordinary worker rig.");
            var host = new GameObject("Fair Vendors");
            host.transform.SetParent(parent, false);
            var vendors = host.AddComponent<CityFairVendors>();
            vendors.plan = fair;
            vendors.block = new MaterialPropertyBlock();
            vendors.Actors = new VillageResidentPresentation[fair.Stalls.Length];
            vendors.spines = new Transform[fair.Stalls.Length];
            vendors.clothing = new Renderer[fair.Stalls.Length][];
            vendors.colors = new Color[fair.Stalls.Length][];
            for (int i = 0; i < fair.Stalls.Length; i++)
            {
                CityFairStall stall = fair.Stalls[i];
                // The library contributes only its passive rig and sampler;
                // no village tasks, greetings or player interaction are copied.
                VillageResidentPresentation actor = library.Create(
                    VillageResidentRole.StationWorker, host.transform);
                actor.name = stall.Id + "-vendor";
                actor.transform.SetPositionAndRotation(stall.VendorPosition,
                    Quaternion.LookRotation(stall.Facing, Vector3.up));
                actor.transform.localScale *= Heights[i];
                CityPortCrew.AlignWorkerModelWithPlacement(actor);
                var body = actor.gameObject.AddComponent<CapsuleCollider>();
                body.center = Vector3.up * .94f;
                body.height = 1.62f;
                body.radius = .21f;
                vendors.Actors[i] = actor;
                vendors.spines[i] = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "spine")
                    ?? throw new InvalidOperationException("The fair worker has no shared spine joint.");
                vendors.clothing[i] = Array.FindAll(actor.GetComponentsInChildren<Renderer>(true),
                    renderer => renderer.name.StartsWith("CLO_", StringComparison.Ordinal));
                vendors.colors[i] = new Color[vendors.clothing[i].Length];
                for (int j = 0; j < vendors.clothing[i].Length; j++)
                {
                    Renderer renderer = vendors.clothing[i][j];
                    renderer.GetPropertyBlock(vendors.block);
                    Color source = vendors.block.GetColor(BaseColor);
                    if (source.a <= 0f && renderer.sharedMaterial != null &&
                        renderer.sharedMaterial.HasProperty(BaseColor))
                        source = renderer.sharedMaterial.GetColor(BaseColor);
                    string part = renderer.name;
                    Color tint = part.Contains("Cap") || part.Contains("Scarf") ||
                        part.Contains("Collar") || part.Contains("Cuff") ? KnitTints[i] :
                        part.Contains("Thigh") || part.Contains("Shin") ?
                        new Color(.88f, .89f, .91f) : CoatTints[i];
                    Color color = source * tint;
                    color.a = 1f;
                    vendors.colors[i][j] = color;
                    vendors.block.Clear();
                }
            }
            vendors.initialized = true;
            vendors.ApplyAt(0f);
            return vendors;
        }

        private void LateUpdate()
        {
            if (initialized) ApplyAt(ElapsedSeconds + Mathf.Max(0f, Time.deltaTime));
        }

        /// <summary>Seekable pose; scaled time freezes both sampling and hand work on pause.</summary>
        public void ApplyAt(float seconds)
        {
            if (!initialized) return;
            ElapsedSeconds = Mathf.Max(0f, seconds);
            HandsMatch = true;
            for (int i = 0; i < Actors.Length; i++)
            {
                VillageResidentPresentation actor = Actors[i];
                float time = ElapsedSeconds + i * 4.17f;
                actor.Apply(VillageResidentAction.Idle, time * (.87f + i * .04f));
                float phase = Mathf.Repeat(time, 19f);
                float weight = Smooth(phase / 2f) * Smooth((7f - phase) / 2f);
                // A small, continuous lean gives the hand a human reach to
                // the counter's rear lip; the hips and feet stay planted.
                spines[i].rotation = Quaternion.AngleAxis(18f * weight,
                    actor.transform.right) * spines[i].rotation;
                CityFairStall stall = plan.Stalls[i];
                // Counter rear edge .025, top .95; goods begin at Z .30.
                // The palm stays on that empty rear strip throughout the wipe.
                Vector3 right = stall.Position + stall.Rotation * new Vector3(
                    .19f + .085f * Mathf.Sin(time * 1.65f), .965f, .065f);
                if (weight > 0f)
                    HandsMatch &= actor.ApplyHandContacts(right, null, weight);
                Tint(i);
            }
        }

        private void Tint(int index)
        {
            for (int i = 0; i < clothing[index].Length; i++)
            {
                Renderer renderer = clothing[index][i];
                renderer.GetPropertyBlock(block);
                block.SetColor(BaseColor, colors[index][i]);
                block.SetColor(LegacyColor, colors[index][i]);
                renderer.SetPropertyBlock(block);
                block.Clear();
            }
        }

        private static float Smooth(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * (3f - 2f * t);
        }
    }
}
