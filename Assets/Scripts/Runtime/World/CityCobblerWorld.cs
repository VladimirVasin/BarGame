using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>One immobile vehicle shell, the practical on its desk and the sliding shop door.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(235)]
    public sealed class CityCobblerWorld : MonoBehaviour
    {
        private const float DoorTravel = 1.38f;
        private const uint SignLightLayer = 1u << 2;
        private const float SignBeamDegrees = 153f;
        private static Texture2D signDiffuser;
        private Transform door, player;
        private Vector3 closedPosition;
        private CityCobblerWorker worker;
        private bool leaving;
        public float DoorOpenness { get; private set; }
        public CityCobblerSitePlan Plan { get; private set; }

        public static Transform Build(Transform parent, CityCobblerSitePlan plan, bool interactive, out Collider rainShelter)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            rainShelter = null;
            if (!plan.IsEnabled) return null;
            var root = new GameObject("Ridge Cobbler Workshop").transform;
            root.SetParent(parent, false);
            root.localPosition = plan.Origin; root.localRotation = plan.Rotation;
            var shell = CityCobblerAssetProvider.Create(root, interactive, out rainShelter);
            CityCobblerYardWorld.Build(root, plan, interactive);
            FitSupports(root, shell, plan);
            LightWorkbench(root, shell);
            LightSign(root, shell);
            var shop = root.gameObject.AddComponent<CityCobblerWorld>();
            shop.Plan = plan;
            shop.door = CityCobblerAssetProvider.FindPart(shell, "PIVOT_Door");
            shop.closedPosition = root.InverseTransformPoint(shop.door.position);
            if (interactive)
            {
                shop.worker = root.gameObject.AddComponent<CityCobblerWorker>();
                shop.worker.Configure(plan, shell);
            }
            root.GetComponentInChildren<CityCobblerWorkLight>().BindWorker(shop.worker);
            shop.SetDoor(shop.OpenForShift() ? 1f : 0f);
            return root;
        }

        public void BindPlayer(Transform observer) => player = observer;

        private static void FitSupports(Transform root, Transform shell, CityCobblerSitePlan plan)
        {
            var ground = CityHillsideGroundPlan.ForLayout(plan.Layout);
            foreach (string name in new[] { "Support_FL", "Support_FR", "Support_RL", "Support_RR" })
            {
                var support = CityCobblerAssetProvider.FindPart(shell, name);
                // Support empties pivot at the top of the block. Read the imported
                // axis rather than assuming that its local Y came through the FBX.
                Vector3 metric = root.InverseTransformPoint(support.position);
                Vector3 point = plan.ToWorld(metric);
                if (!ground.TrySampleTop(new Vector2(point.x, point.z), out float top))
                    throw new InvalidOperationException("Missing earth at cobbler " + name);
                float height = point.y - top + .045f;
                if (height < .08f || height > .85f)
                    throw new InvalidOperationException("Cobbler footing is outside its block height at " + point);
                Vector3 vertical = support.InverseTransformDirection(root.up);
                int axis = Mathf.Abs(vertical.x) > Mathf.Abs(vertical.y) ? 0 : 1;
                if (Mathf.Abs(vertical.z) > Mathf.Abs(vertical[axis])) axis = 2;
                Vector3 scale = support.localScale;
                scale[axis] *= height / .40f;
                support.localScale = scale;
            }
        }

        private static void LightWorkbench(Transform root, Transform shell)
        {
            var source = CityCobblerAssetProvider.FindPart(shell, "ANCHOR_Lamp");
            var target = CityCobblerAssetProvider.FindPart(shell, "ANCHOR_LampAim");
            var fixture = new GameObject("Cobbler desk light").transform;
            fixture.SetParent(root, false);
            fixture.SetPositionAndRotation(source.position, Quaternion.LookRotation(target.position - source.position));
            var lamp = fixture.gameObject.AddComponent<Light>();
            lamp.type = LightType.Spot;
            lamp.color = new Color(1f, .83f, .62f);
            lamp.range = 2.85f;
            lamp.spotAngle = 112f;
            lamp.innerSpotAngle = 64f;
            lamp.shadows = LightShadows.Soft;
            lamp.shadowStrength = .90f;
            lamp.shadowBias = .015f;
            lamp.shadowNormalBias = .035f;
            lamp.shadowNearPlane = .05f;
            var settings = lamp.GetUniversalAdditionalLightData();
            settings.usePipelineSettings = false;
            if (Application.isPlaying)
                settings.additionalLightsShadowResolutionTier = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
            // A few centimetres of lit air stay inside the shade and share the City's daylight floor.
            var halo = fixture.gameObject.AddComponent<CityLightHalo>();
            halo.Initialize(CityNightResources.AtmosphereMaterial, .047f, .15f,
                new Color(.96f, .76f, .48f, .22f), new Color(.84f, .62f, .36f, .055f));
            var lens = CityCobblerAssetProvider.FindPart(shell, "CobblerBus__Lens").GetComponent<Renderer>();
            fixture.gameObject.AddComponent<CityCobblerWorkLight>().Initialize(lamp, lens, halo);
        }

        private static void LightSign(Transform root, Transform shell)
        {
            var source = CityCobblerAssetProvider.FindPart(shell, "ANCHOR_SignLamp");
            var aim = CityCobblerAssetProvider.FindPart(shell, "ANCHOR_SignAim");
            var fixture = new GameObject("Cobbler sign light").transform;
            fixture.SetParent(root, false);
            fixture.SetPositionAndRotation(source.position, Quaternion.LookRotation(aim.position - source.position, root.up));
            var light = fixture.gameObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = SignBeamDegrees;
            light.innerSpotAngle = 145f;
            light.range = 2.2f;
            light.color = new Color(1f, .91f, .78f);
            light.shadows = LightShadows.None;
            light.GetUniversalAdditionalLightData().renderingLayers = SignLightLayer;
            // Only the painted plate and its raised letters catch this hooded strip.
            // Both retain their ordinary City lighting and shared matte atlas.
            foreach (var name in new[] { "SignPanel__Paint", "SignLetters__Paint" })
                CityCobblerAssetProvider.FindPart(shell, name).GetComponent<Renderer>().renderingLayerMask |= SignLightLayer;
            light.cookie = SignDiffuser(fixture.InverseTransformDirection(root.right));
            CityNightSiteLightRegistry.Register(light, .30f, null);
        }

        private static Texture2D SignDiffuser(Vector3 boardNormal)
        {
            if (signDiffuser) return signDiffuser;
            const int size = 64;
            signDiffuser = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "Cobbler sign frosted strip", filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[size * size];
            float spread = Mathf.Tan(SignBeamDegrees * .5f * Mathf.Deg2Rad);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var ray = new Vector3((2f * (x + .5f) / size - 1f) * spread,
                    (2f * (y + .5f) / size - 1f) * spread, 1f).normalized;
                // A nearby point would burn a hot dot in the middle of the long sign.
                // On its plane, inverse square and incidence combine as cos³.
                float incidence = Mathf.Max(.01f, -Vector3.Dot(ray, boardNormal));
                float transmission = Mathf.Clamp01(.059f / (incidence * incidence * incidence));
                byte v = (byte)Mathf.RoundToInt(255f * transmission);
                pixels[y * size + x] = new Color32(v, v, v, v);
            }
            signDiffuser.SetPixels32(pixels);
            signDiffuser.Apply(false, true);
            return signDiffuser;
        }

        private bool OpenForShift()
        {
            if (worker) return worker.DoorShouldBeOpen;
            return GameSessionState.GameTimeOfDayMinutes >= CityCobblerWorker.ShiftStartMinute &&
                GameSessionState.GameTimeOfDayMinutes < CityCobblerWorker.ShiftEndMinute;
        }

        private void LateUpdate()
        {
            if (!door || GameTimeScaleRuntime.IsPaused) return;
            bool open = OpenForShift();
            if (open && DoorOpenness == 1f) return;
            if (!open && player)
            {
                Vector3 p = transform.InverseTransformPoint(player.position);
                // If closing time catches a visitor, keep the leaf open until their
                // whole capsule leaves both the doorway and the two outside treads.
                bool inCabin = Mathf.Abs(p.x) < 1.17f && Mathf.Abs(p.z) < 4.04f;
                bool atEntry = p.x > .50f && p.x < 2.78f && p.z > 1.36f && p.z < 3.57f;
                bool atFloor = p.y > -.45f && p.y < 2.9f;
                if (!atFloor || (!inCabin && !atEntry)) leaving = false;
                else if (inCabin || (atEntry && DoorOpenness > .001f)) leaving = true;
                open |= leaving;
            }
            float next = Mathf.MoveTowards(DoorOpenness, open ? 1f : 0f, Time.deltaTime * 2.5f);
            if (next != DoorOpenness) SetDoor(next);
        }

        private void SetDoor(float fraction)
        {
            DoorOpenness = fraction;
            // The imported pivot's parent may carry scaled and rotated FBX axes.
            // Drive its world position along the metre root and preserve the authored rotation.
            door.position = transform.TransformPoint(closedPosition + Vector3.back * (DoorTravel * fraction));
        }
    }
}
