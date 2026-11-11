using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public static class CityGlassReturnWorld
    {
        private static Texture2D floodlightCookie;
        public static Transform Build(Transform parent, CityGlassReturnSitePlan plan, bool interactive, out Collider rainShelter)
        {
            rainShelter = null;
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.IsEnabled) return null;
            Transform shell = CityGlassReturnAssetProvider.CreatePavilion(parent).transform;
            shell.name = "Ridge Glass Return";
            shell.localPosition = plan.Origin; shell.localRotation = plan.Rotation;
            CityRainDripEdge.Add(shell, new Vector3(-1.70f, 2.287f, 2.382f), new Vector3(1.70f, 2.287f, 2.382f));
            Transform yard = CityGlassReturnAssetProvider.CreateYard(parent).transform;
            yard.localPosition = plan.YardOrigin; yard.localRotation = Quaternion.identity;

            Transform source = CityGlassReturnAssetProvider.FindPart(shell, "ANCHOR_WorkLight");
            Transform aim = CityGlassReturnAssetProvider.FindPart(shell, "ANCHOR_LightTarget");
            Transform lamp = new GameObject(CityNightAtmosphere.IndustrialTaskLightPrefix + "GlassReturnWindow").transform;
            lamp.SetParent(shell, false);
            lamp.SetPositionAndRotation(source.position, Quaternion.LookRotation(aim.position - source.position));
            var lens = CityGlassReturnAssetProvider.FindPart(shell, "LampLens").GetComponent<Renderer>();
            lens.sharedMaterial = CityNightResources.EmissiveMaterial;
            lens.shadowCastingMode = ShadowCastingMode.Off;
            var workLight = lamp.gameObject.AddComponent<CityGlassReturnWorkLight>();
            workLight.Initialize(lens, new Color(2.6f, 2.16f, 1.52f));
            foreach (string side in new[] { "Left", "Right" })
            {
                Transform signSource = CityGlassReturnAssetProvider.FindPart(shell, "ANCHOR_SignLight" + side);
                Transform signTarget = CityGlassReturnAssetProvider.FindPart(shell, "ANCHOR_SignTarget" + side);
                Transform signLamp = new GameObject(CityNightAtmosphere.IndustrialTaskLightPrefix + "GlassReturnSign" + side).transform;
                signLamp.SetParent(shell, false);
                signLamp.SetPositionAndRotation(signSource.position, Quaternion.LookRotation(signTarget.position - signSource.position));
            }
            Transform grilleRoot = NormalizePivot(shell, "MOVE_WindowGrille");
            var grille = new CityGlassReturnGrille(grilleRoot, interactive);
            NormalizePivot(shell, "MOVE_SwitchRocker");
            BuildFloodlight(parent, plan);
            if (!interactive)
            {
                workLight.BindPassiveGrille(grille);
                return shell;
            }

            // Keep the wide window physically open down to the sill. A single volume
            // filling the whole shed would let the camera collide with thin air at the counter.
            Box(shell, "Plinth", new(0f,-.225f,0f), new(4.6f,.45f,3.2f));
            Box(shell, "Rear wall", new(0f,1.35f,-1.51f), new(4.6f,2.7f,.18f));
            for (int side = -1; side <= 1; side += 2)
            {
                Box(shell, "Window pier", new(side*1.805f,1.35f,1.48f), new(.99f,2.7f,.18f));
                Box(yard, "Side fence", new(side*2.045f,.81f,0f), new(.08f,1.62f,3.75f));
                Box(yard, "End fence", new(0f,.81f,side*1.845f), new(4.16f,1.62f,.08f));
            }
            Box(shell, "Side wall", new(-2.15f,1.35f,0f), new(.18f,2.7f,2.8f));
            Box(shell, "Service wall rear", new(2.15f,1.35f,-1.17f), new(.18f,2.7f,.46f));
            Box(shell, "Service wall front", new(2.15f,1.35f,.69f), new(.18f,2.7f,1.42f));
            Box(shell, "Service lintel", new(2.15f,2.42f,-.48f), new(.18f,.56f,.92f));
            // Imported FBX empties retain their authoring axes/scale. The
            // moving collider and worker rotate a metre-scale vertical hinge.
            Transform door = NormalizePivot(shell, "MOVE_ServiceDoor");
            Box(door, "Service leaf", new(-.018f,1.067f,-.46f), new(.036f,2.126f,.91f));
            Box(shell, "Window sill", new(0f,.485f,1.48f), new(2.62f,.97f,.18f));
            Box(shell, "Receiving ledge", new(0f,.985f,1.535f), new(2.64f,.07f,.50f));
            Box(shell, "Window lintel", new(0f,2.45f,1.48f), new(2.62f,.50f,.18f));
            Box(shell, "Roof", new(0f,2.755f,0f), new(4.60f,.11f,3.2f));
            rainShelter = Box(shell, "Steel canopy", new(0f,2.33f,1.94f), new(3.58f,.105f,.88f));
            Box(yard, "Yard foundation", new(0f,-.225f,0f), new(4.2f,.45f,3.8f));
            var worker = shell.gameObject.AddComponent<CityGlassReturnWorker>();
            worker.BindWindowGrille(grille);
            worker.Initialize(shell);
            workLight.BindWorker(worker);
            return shell;
        }

        internal static Transform NormalizePivot(Transform parent, string name)
        {
            Transform imported = CityGlassReturnAssetProvider.FindPart(parent, name);
            imported.name = "Authored " + name;
            Transform pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.position = imported.position;
            imported.SetParent(pivot, true);
            return pivot;
        }

        private static void BuildFloodlight(Transform parent, CityGlassReturnSitePlan plan)
        {
            Transform fixture = CityGlassReturnAssetProvider.CreateFloodlight(parent).transform;
            fixture.name = "Glass Return Wall Floodlight";
            fixture.localPosition = plan.FloodlightWallContact + Vector3.right * .18f;
            fixture.localRotation = Quaternion.LookRotation(Vector3.right);
            Transform head = CityGlassReturnAssetProvider.FindPart(fixture, "MOVE_Head");
            Vector3 target = parent.TransformPoint(plan.FloodlightTarget);
            head.rotation = Quaternion.LookRotation(target - head.position);
            Transform source = CityGlassReturnAssetProvider.FindPart(fixture, "ANCHOR_Source");
            Transform lamp = new GameObject(CityNightAtmosphere.IndustrialTaskLightPrefix + "GlassReturnFloodlight").transform;
            lamp.SetParent(fixture, false);
            lamp.SetPositionAndRotation(source.position, head.rotation);
            var lens = CityGlassReturnAssetProvider.FindPart(fixture, "LampLens").GetComponent<Renderer>();
            lens.sharedMaterial = CityNightResources.EmissiveMaterial;
            lens.shadowCastingMode = ShadowCastingMode.Off;
            CityNightGlowRegistry.Register(lens, new Color(3.3f, 3.6f, 3.7f));
        }

        internal static Texture2D FloodlightCookie(Transform anchor, float spotAngle)
        {
            if (floodlightCookie) return floodlightCookie;
            // Most light goes down into the parcel. Grazing spill from the
            // frosted lens reaches the masonry. Steeper, near-mount rays are
            // attenuated to compensate for inverse-square falloff and incidence.
            const int size = 64;
            floodlightCookie = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "Glass Return Frosted Reflector",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            Vector3 normal = anchor.InverseTransformDirection(Vector3.right);
            float tangent = Mathf.Tan(spotAngle * .5f * Mathf.Deg2Rad);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector3 ray = new Vector3((2f * x / (size - 1) - 1f) * tangent,
                    (2f * y / (size - 1) - 1f) * tangent, 1f).normalized;
                float outward = Vector3.Dot(normal, ray);
                float incidence = Mathf.Max(0f, -outward);
                float slope = incidence / .17f;
                float spill = .12f / (1f + slope * slope * slope);
                spill *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.36f, .65f, incidence));
                float forward = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.08f, .34f, outward));
                byte value = (byte)Mathf.RoundToInt(255f * Mathf.Lerp(spill, 1f, forward));
                pixels[y * size + x] = new Color32(value, value, value, value);
            }
            floodlightCookie.SetPixels32(pixels);
            floodlightCookie.Apply(false, true);
            return floodlightCookie;
        }

        private static BoxCollider Box(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            var collider = host.AddComponent<BoxCollider>();
            collider.center = center; collider.size = size;
            return collider;
        }
    }

    /// <summary>The linked steel rows follow the guides onto the drum.
    /// The worker's switch and motor own time; this binding applies one pose.</summary>
    public sealed class CityGlassReturnGrille
    {
        private readonly Transform root;
        private readonly Transform[] rows = new Transform[13];
        private readonly Renderer[] sections = new Renderer[13];
        private readonly float travel, pitch;
        private const float Width = 2.58f, Depth = .043f;
        public BoxCollider Barrier { get; }
        public float ClosedAmount { get; private set; } = -1f;

        internal CityGlassReturnGrille(Transform root, bool interactive)
        {
            this.root = root;
            Transform mouth = CityGlassReturnAssetProvider.FindPart(root, "ANCHOR_GrilleMouth");
            travel = root.InverseTransformPoint(mouth.position).y;
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = CityGlassReturnWorld.NormalizePivot(root, $"GrilleRow{i:00}");
                sections[i] = rows[i].GetComponentInChildren<MeshRenderer>();
            }
            pitch = rows[1].localPosition.y - rows[0].localPosition.y;
            if (interactive) Barrier = root.gameObject.AddComponent<BoxCollider>();
            TrySetClosedAmount(0f, force: true);
        }

        public bool TrySetClosedAmount(float amount, CharacterController occupant = null, bool force = false)
        {
            if (!float.IsFinite(amount)) throw new ArgumentOutOfRangeException(nameof(amount));
            float next = Mathf.Clamp01(amount);
            if (next == ClosedAmount) return true;
            float bottom = travel * (1f - next);
            if (!force && next > ClosedAmount && occupant && occupant.enabled)
            {
                Bounds body = occupant.bounds;
                Vector3 center = root.InverseTransformPoint(body.center);
                float radius = occupant.radius * Mathf.Max(Mathf.Abs(occupant.transform.lossyScale.x),
                    Mathf.Abs(occupant.transform.lossyScale.z));
                if (Mathf.Abs(center.x) <= Width * .5f + radius && Mathf.Abs(center.z) <= Depth * .5f + radius &&
                    center.y + body.extents.y >= bottom && center.y - body.extents.y <= travel * (1f - ClosedAmount))
                    return false;
            }
            ClosedAmount = next;
            const float radiusAtMouth = .048f, radiusGrowth = .0020f;
            for (int i = 0; i < rows.Length; i++)
            {
                float height = bottom + (i + .5f) * pitch;
                float wound = Mathf.Max(0f, height - travel);
                float angle = 2f * wound / (radiusAtMouth +
                    Mathf.Sqrt(radiusAtMouth * radiusAtMouth + 2f * radiusGrowth * wound));
                float radius = radiusAtMouth + radiusGrowth * angle;
                Vector3 position = wound > 0f ? new Vector3(0f, travel + radius * Mathf.Sin(angle),
                    radiusAtMouth - radius * Mathf.Cos(angle)) : new Vector3(0f, height, 0f);
                rows[i].SetLocalPositionAndRotation(position, Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.right));
                // At full retraction even the leading bar is inside the cassette.
                sections[i].enabled = next > 0f;
            }
            if (Barrier)
            {
                Barrier.enabled = next > 0f;
                Barrier.center = new Vector3(0f, (bottom + travel) * .5f, 0f);
                Barrier.size = new Vector3(Width, Mathf.Max(.001f, travel - bottom), Depth);
            }
            return true;
        }
    }
}
