using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Session equipment and owned mouth access on the one production hero.</summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class PlayerScarfController : MonoBehaviour
    {
        public const float ReachSeconds = 0.24f;
        public const float PullSeconds = 0.55f;
        public const float ReleaseSeconds = 0.24f;
        public const float GestureSeconds = ReachSeconds + PullSeconds + ReleaseSeconds;

        public sealed class MouthAccess : IDisposable
        {
            private PlayerScarfController controller;
            internal readonly UnityEngine.Object Owner;
            internal MouthAccess(PlayerScarfController value, UnityEngine.Object owner)
            { controller = value; Owner = owner; }
            public bool IsReady => controller == null || !controller.isActiveAndEnabled ||
                !controller.access.Contains(this) || controller.IsMouthReady;
            public void Dispose()
            {
                PlayerScarfController value = controller;
                controller = null;
                if (value != null) value.access.Remove(this);
            }
        }

        private readonly List<MouthAccess> access = new List<MouthAccess>();
        private PlayerRuntime player;
        private Player3DCharacterPresentation visual;
        private CityGameRoot city;
        private MountainRoadRoot road;
        private AlpineVillageRoot village;
        private HomeInteriorRoot home;
        private Transform upperArm, forearm, hand, grip;
        private bool requestedLower, gestureActive, armApplied;
        private float elapsed, startLower, targetLower;
        private Quaternion upperBase, forearmBase, handBase;
        private Quaternion gripInHand;
        private readonly List<Vector3> handSurfaceFromGrip = new List<Vector3>();

        public PlayerScarfPresentation Presentation { get; private set; }
        public bool IsMouthReady => Presentation == null || !Presentation.IsEquipped ||
            (Presentation.MouthLowered >= 0.999f && !gestureActive);
        public bool IsGestureActive => Presentation != null && Presentation.IsEquipped &&
            (gestureActive || (access.Count > 0) != requestedLower);
        public float HandContactError { get; private set; }
        public float GestureProgress => Mathf.Clamp01(elapsed / GestureSeconds);

        public static PlayerScarfController Install(PlayerRuntime runtime)
        {
            if (!(runtime.Visual is Player3DCharacterPresentation presentation)) return null;
            var controller = runtime.GameObject.AddComponent<PlayerScarfController>();
            controller.player = runtime;
            controller.visual = presentation;
            controller.Presentation = PlayerScarfPresentation.Install(presentation.Registry);
            presentation.RegisterAccessoryRenderers(controller.Presentation.Renderers);
            controller.city = runtime.GameObject.GetComponentInParent<CityGameRoot>();
            controller.road = runtime.GameObject.GetComponentInParent<MountainRoadRoot>();
            controller.village = runtime.GameObject.GetComponentInParent<AlpineVillageRoot>();
            controller.home = runtime.GameObject.GetComponentInParent<HomeInteriorRoot>();
            controller.upperArm = controller.Bone(Player3DAnatomicalPart.LeftUpperArm);
            controller.forearm = controller.Bone(Player3DAnatomicalPart.LeftForearm);
            controller.hand = controller.Bone(Player3DAnatomicalPart.LeftHand);
            controller.grip = presentation.Registry.Anchors.LeftGrip;
            if (controller.hand != null && controller.grip != null)
            {
                controller.gripInHand = Quaternion.Inverse(controller.hand.rotation) * controller.grip.rotation;
                controller.CacheHandContactSurface();
            }
            GameSessionState.InventoryEquipmentChanged += controller.RefreshEquipment;
            runtime.Interactor?.SetInteractionFilter(controller, controller.AllowsInteraction);
            controller.RefreshEquipment();
            controller.RefreshEnvironmentAndVisibility();
            return controller;
        }

        private void CacheHandContactSurface()
        {
            // The generic object grip lies inside the palm. Fabric must touch
            // the hand's outside surface, including the thumb, instead.
            var scratch = new Mesh { name = "Scarf Hand Contact Bake", hideFlags = HideFlags.HideAndDontSave };
            try
            {
                Quaternion toHand = Quaternion.Inverse(hand.rotation);
                foreach (Player3DMeshBinding binding in visual.Registry.MeshBindings)
                {
                    if (binding == null || binding.Bone != hand || binding.Renderer == null) continue;
                    Renderer renderer = binding.Renderer;
                    Vector3[] vertices;
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        skinned.BakeMesh(scratch, true);
                        vertices = scratch.vertices;
                    }
                    else
                    {
                        Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                        if (mesh == null) continue;
                        PlayerScarfCollisionWorld.ReadMesh(mesh, out vertices, out _);
                    }
                    foreach (Vector3 vertex in vertices)
                        handSurfaceFromGrip.Add(toHand * (renderer.transform.TransformPoint(vertex) - grip.position));
                }
            }
            finally { PlayerScarfResources.DestroyOwned(scratch); }
        }

        public static MouthAccess RequireMouthAccess(PlayerRuntime runtime, UnityEngine.Object owner) =>
            RequireMouthAccess(runtime.GameObject, owner);

        public static MouthAccess RequireMouthAccess(GameObject actor, UnityEngine.Object owner)
        {
            PlayerScarfController controller = actor != null ? actor.GetComponent<PlayerScarfController>() : null;
            if (controller != null && !controller.isActiveAndEnabled) controller = null;
            var lease = new MouthAccess(controller, owner);
            if (controller != null) controller.access.Add(lease);
            return lease;
        }

        private Transform Bone(Player3DAnatomicalPart part) =>
            visual.Registry.TryGetPart(part, out Player3DAnatomicalPartBinding binding) ? binding.Bone : null;

        private bool AllowsInteraction(IInteractable candidate) => !IsGestureActive;

        private void OnEnable() => player.Interactor?.SetInteractionFilter(this, AllowsInteraction);

        private void RefreshEquipment()
        {
            if (Presentation == null) return;
            bool equipped = GameSessionState.IsInventoryItemEquipped(InventoryItemId.Scarf);
            Presentation.SetEquipped(equipped);
            if (!equipped)
            {
                RestoreGestureArm();
                requestedLower = gestureActive = false;
                Presentation.SetMouthLowered(0f);
            }
        }

        private void LateUpdate()
        {
            if (Presentation == null) return;
            for (int index = access.Count - 1; index >= 0; index--)
                if (access[index].Owner == null) access.RemoveAt(index);
            bool lower = access.Count > 0 && Presentation.IsEquipped;
            if (lower != requestedLower)
            {
                requestedLower = lower;
                startLower = Presentation.MouthLowered;
                targetLower = lower ? 1f : 0f;
                elapsed = 0f;
                gestureActive = true;
            }
            // The base rig is sampled every frame before this pass.
            armApplied = false;
            if (gestureActive)
            {
                if (!PauseMenuController.IsAnyPaused && !SceneTransitionService.IsTransitioning)
                    elapsed = Mathf.Min(GestureSeconds, elapsed + Time.deltaTime);
                float pull = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - ReachSeconds) / PullSeconds));
                Presentation.SetMouthLowered(Mathf.Lerp(startLower, targetLower, pull));
                ApplyGestureArm();
                if (elapsed >= GestureSeconds) gestureActive = false;
            }
            RefreshEnvironmentAndVisibility();
        }

        private void ApplyGestureArm()
        {
            if (upperArm == null || forearm == null || hand == null || grip == null ||
                Player3DBathingAppearance.IsActive || player.Ragdoll != null && player.Ragdoll.IsActive) return;
            float weight = elapsed < ReachSeconds ? Mathf.SmoothStep(0f, 1f, elapsed / ReachSeconds) :
                1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - ReachSeconds - PullSeconds) / ReleaseSeconds));
            if (weight <= 0f) return;
            upperBase = upperArm.localRotation;
            forearmBase = forearm.localRotation;
            handBase = hand.localRotation;
            Vector3 head = visual.Registry.Anchors.Head.position;
            Vector3 forward = (visual.Registry.Anchors.Mouth.position - head).normalized;
            Vector3 up = transform.up;
            Vector3 lateral = Vector3.ProjectOnPlane(upperArm.position - visual.Registry.Anchors.Chest.position, up).normalized;
            Vector3 contact = Presentation.GetFrontGripPosition(Presentation.MouthLowered);
            Quaternion rotation = Quaternion.LookRotation(up, -forward) * Quaternion.Inverse(gripInHand);
            Vector3 handSurface = Vector3.zero;
            float nearest = float.PositiveInfinity;
            foreach (Vector3 vertex in handSurfaceFromGrip)
            {
                float projection = Vector3.Dot(rotation * vertex, forward);
                if (projection >= nearest) continue;
                nearest = projection;
                handSurface = vertex;
            }
            Vector3 surfaceTarget = contact + forward * PlayerScarfContactSolver.Thickness;
            Vector3 gripTarget = surfaceTarget - rotation * handSurface;
            Vector3 socketOffset = Quaternion.Inverse(hand.rotation) * (grip.position - hand.position);
            LimbTwoBoneIk.Solve(upperArm, forearm, hand, gripTarget - rotation * socketOffset,
                rotation, upperArm.position + lateral * 0.30f + forward * 0.23f - up * 0.18f,
                weight, LimbTwoBoneIk.DefaultReachFraction, true);
            HandContactError = Vector3.Distance(grip.position + hand.rotation * handSurface, surfaceTarget);
            armApplied = true;
        }

        private void RestoreGestureArm()
        {
            if (!armApplied) return;
            if (upperArm != null) upperArm.localRotation = upperBase;
            if (forearm != null) forearm.localRotation = forearmBase;
            if (hand != null) hand.localRotation = handBase;
            armApplied = false;
        }

        private void RefreshEnvironmentAndVisibility()
        {
            bool outside = false;
            WindSample wind = new WindSample(0f, 0f);
            if (city != null && city.Weather != null)
            { outside = true; wind = city.Weather.CurrentWind; }
            else if (road != null && road.Weather != null)
            { outside = road.CabinSeat == null || !road.CabinSeat.IsSeated; wind = road.Weather.CurrentWind; }
            else if (village != null && village.Weather != null)
            {
                outside = (village.CabinSeat == null || !village.CabinSeat.IsSeated) &&
                    (village.Workroom == null || !village.Workroom.Environment.IsInside);
                wind = village.Weather.CurrentWind;
            }
            else if (home != null && home.BalconyLayout != null)
            {
                Vector3 point = home.transform.InverseTransformPoint(transform.position);
                outside = home.BalconyLayout.BalconyBounds.Contains(new Vector2(point.x, point.z));
                wind = GameWeatherRules.EvaluateCurrentWind();
            }
            outside &= !GameSessionState.IsRidingAVehicle && !SceneTransitionService.IsTransitioning;
            Presentation.SetEnvironment(outside, outside ? wind : new WindSample(0f, 0f));
            Presentation.SetTemporaryRemoved(Player3DBathingAppearance.IsActive);
            Presentation.SyncVisibility(Player3DHeadVisibility.IsHeadDrawn(visual.Registry),
                player.PresentationVisibility == null || !player.PresentationVisibility.RenderersHidden);
        }

        private void OnDisable()
        {
            player.Interactor?.SetInteractionFilter(this, null);
            access.Clear();
            RestoreGestureArm();
            requestedLower = gestureActive = false;
            if (Presentation != null)
            {
                Presentation.SetMouthLowered(0f);
                Presentation.SetEnvironment(false, new WindSample(0f, 0f));
                Presentation.SyncVisibility(false, false);
            }
        }

        private void OnDestroy() => GameSessionState.InventoryEquipmentChanged -= RefreshEquipment;
    }
}
