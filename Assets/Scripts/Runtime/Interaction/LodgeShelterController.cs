using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>Binary prop interactions. No camera, hero pose, inventory or time ownership.</summary>
    public sealed class LodgeShelterController : MonoBehaviour
    {
        private AlpineVillageRoot village;
        private readonly Transform[] hinges = new Transform[2];
        private readonly Bounds[] closedBounds = new Bounds[2];
        private readonly LodgeShelterInteraction[] doors = new LodgeShelterInteraction[2];
        private Renderer mantle;
        private MaterialPropertyBlock lampProperties;
        private readonly RaycastHit[] sightHits = new RaycastHit[24];
        private CharacterController playerBody;

        public int OpenDoorCount => (LodgeShelterSessionState.LeftDoorOpen ? 1 : 0) +
            (LodgeShelterSessionState.RightDoorOpen ? 1 : 0);
        public Light LanternLight { get; private set; }
        public LodgeShelterInteraction Cot { get; private set; }
        public LodgeShelterInteraction Kettle { get; private set; }
        public LodgeShelterInteraction Lantern { get; private set; }
        public LodgeShelterInteraction Door(int index) => doors[index];
        public Transform Hinge(int index) => hinges[index];

        public void Initialize(AlpineVillageRoot owner)
        {
            village = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            lampProperties = new MaterialPropertyBlock();
            playerBody = village.Player.GameObject.GetComponent<CharacterController>();
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? "Left" : "Right";
                hinges[i] = Anchor("LodgeDoor" + side + "Hinge");
                var panel = Anchor("LodgeDoor" + side + "Panel").GetComponent<MeshFilter>();
                if (panel == null) throw new InvalidOperationException("Missing solid lodge door panel.");
                closedBounds[i] = MeasureInRoom(panel);
                // The placeholder is reachable at either side of its half of
                // the doorway, even when the leaf is parked outside the wall.
                // A future hand action can use the authored two-sided handles.
                var dock = new GameObject("Lodge Door " + side + " Interaction");
                dock.transform.SetParent(transform, false);
                Vector3 centre = closedBounds[i].center;
                dock.transform.localPosition = new Vector3(centre.x, .02f, centre.z);
                doors[i] = AddInteraction(dock.transform, i == 0 ? LodgeShelterAction.LeftDoor : LodgeShelterAction.RightDoor);
                // Handles and braces project beyond the core panel and are
                // solid too. They must not appear through the capsule either.
                foreach (MeshCollider solid in hinges[i].GetComponentsInChildren<MeshCollider>())
                    closedBounds[i].Encapsulate(MeasureInRoom(solid.GetComponent<MeshFilter>()));
            }
            Cot = AddInteraction(Anchor("CotInteractionDock"), LodgeShelterAction.Cot);
            Kettle = AddInteraction(Anchor("KettleInteractionDock"), LodgeShelterAction.Kettle);
            Lantern = AddInteraction(Anchor("LanternInteractionDock"), LodgeShelterAction.Lantern);
            mantle = Anchor("LanternMantle").GetComponent<Renderer>();
            LanternLight = Anchor("LanternLightDock").gameObject.AddComponent<Light>();
            LanternLight.type = LightType.Point;
            LanternLight.color = new Color(1f, .74f, .43f);
            LanternLight.range = 6f;
            LanternLight.intensity = 5f;
            LanternLight.shadows = LightShadows.Soft;
            LanternLight.shadowStrength = .85f;
            LanternLight.shadowBias = .025f;
            LanternLight.shadowNormalBias = .08f;
            LanternLight.GetUniversalAdditionalLightData().additionalLightsShadowResolutionTier =
                UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium;
            RestoreState();
        }

        public bool ContainsInterior(Vector3 position)
        {
            Vector3 local = transform.InverseTransformPoint(position);
            return Mathf.Abs(local.x) < 8.68f && local.z > -5.68f && local.z < 5.68f &&
                local.y >= -.3f && local.y < 3.6f;
        }

        public bool ContainsWeatherShelter(Vector3 position)
        {
            Vector3 local = transform.InverseTransformPoint(position);
            // Follow the inside of the pitched roof. Precipitation outside the
            // windows remains visible instead of disappearing in a hero-centred hole.
            return Mathf.Abs(local.x) < 8.68f && Mathf.Abs(local.z) < 5.68f &&
                local.y >= -.3f && local.y < 4.95f - Mathf.Abs(local.z) * .235f;
        }

        public void RestoreState()
        {
            if (village == null) return;
            for (int i = 0; i < 2; i++) ApplyDoor(i);
            ApplyLantern();
            Physics.SyncTransforms();
        }

        public bool TrySetDoorOpen(int index, bool open)
        {
            if (index < 0 || index > 1 || village == null || !isActiveAndEnabled ||
                GameTimeScaleRuntime.IsPaused || SceneTransitionService.IsTransitioning) return false;
            if (LodgeShelterSessionState.IsDoorOpen(index) == open) return true;
            // Check the destination before committing. A binary placeholder must
            // never materialize a closed (or parked open) leaf through the hero.
            if (playerBody != null && playerBody.enabled && OccupiesDestination(index, open)) return false;
            LodgeShelterSessionState.SetDoorOpen(index, open);
            ApplyDoor(index);
            Physics.SyncTransforms();
            return true;
        }

        public bool SetLanternLit(bool lit)
        {
            if (village == null || !isActiveAndEnabled || GameTimeScaleRuntime.IsPaused ||
                SceneTransitionService.IsTransitioning) return false;
            LodgeShelterSessionState.SetLanternLit(lit);
            ApplyLantern();
            return true;
        }

        internal bool CanInteract(PlayerInteractor interactor, LodgeShelterInteraction target)
        {
            if (village == null || village.IsDormant || !isActiveAndEnabled ||
                interactor != village.Player.Interactor || !interactor.isActiveAndEnabled ||
                !interactor.InputEnabled || interactor.InteractKeyClaimed || GameTimeScaleRuntime.IsPaused ||
                BarMinigameModalLock.IsAnyLocked || SceneTransitionService.IsTransitioning ||
                CounterMenuInput.IsBlockedByOtherUi()) return false;
            Vector3 ground = target.InteractionPosition;
            Vector3 difference = ground - interactor.transform.position;
            if (Mathf.Abs(difference.y) > .35f || difference.sqrMagnitude > 1.65f * 1.65f) return false;
            Vector3 start = interactor.transform.position + Vector3.up * .9f;
            Vector3 end = ground + Vector3.up * .9f;
            Vector3 ray = end - start;
            int count = Physics.RaycastNonAlloc(start, ray.normalized, sightHits, ray.magnitude,
                PlayerInteractor.InteractionLayerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform hit = sightHits[i].transform;
                if (hit.IsChildOf(interactor.transform)) continue;
                // A door is reachable from either face; only its own solid parts
                // may stand between the hero and its doorway interaction dock.
                if (target.IsDoor && hit.IsChildOf(hinges[target.DoorIndex])) continue;
                return false;
            }
            return count < sightHits.Length;
        }

        private LodgeShelterInteraction AddInteraction(Transform dock, LodgeShelterAction action)
        {
            var target = dock.gameObject.AddComponent<LodgeShelterInteraction>();
            target.Initialize(this, action);
            var trigger = dock.gameObject.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = .18f;
            // Floor docks are probed at torso height by the shared interactor.
            trigger.center = Vector3.up * .8f;
            return target;
        }

        internal Vector3 InteractionGround(Transform target)
        {
            Vector3 local = transform.InverseTransformPoint(target.position);
            local.y = .02f + PlayerFactory.GroundedRootOffset;
            return transform.TransformPoint(local);
        }

        private bool OccupiesDestination(int index, bool open)
        {
            Bounds bounds = closedBounds[index];
            if (open)
            {
                Vector3 hinge = hinges[index].localPosition;
                Vector3 centre = bounds.center;
                centre.x = 2f * hinge.x - centre.x;
                centre.z = 2f * hinge.z - centre.z;
                bounds.center = centre;
            }
            Vector3 centreLocal = transform.InverseTransformPoint(playerBody.transform.TransformPoint(playerBody.center));
            float radius = playerBody.radius + playerBody.skinWidth;
            if (centreLocal.y + playerBody.height * .5f < bounds.min.y ||
                centreLocal.y - playerBody.height * .5f > bounds.max.y) return false;
            float dx = Mathf.Max(bounds.min.x - centreLocal.x, 0f, centreLocal.x - bounds.max.x);
            float dz = Mathf.Max(bounds.min.z - centreLocal.z, 0f, centreLocal.z - bounds.max.z);
            return dx * dx + dz * dz <= radius * radius;
        }

        private Bounds MeasureInRoom(MeshFilter panel)
        {
            Bounds bounds = default;
            bool first = true;
            foreach (Vector3 vertex in panel.sharedMesh.vertices)
            {
                Vector3 point = transform.InverseTransformPoint(panel.transform.TransformPoint(vertex));
                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                else bounds.Encapsulate(point);
            }
            return bounds;
        }

        private void ApplyDoor(int index) => hinges[index].localRotation = Quaternion.Euler(0f,
            LodgeShelterSessionState.IsDoorOpen(index) ? (index == 0 ? 180f : -180f) : 0f, 0f);

        private void ApplyLantern()
        {
            bool lit = isActiveAndEnabled && LodgeShelterSessionState.LanternLit;
            LanternLight.enabled = lit;
            mantle.sharedMaterial = lit ? CityNightResources.EmissiveMaterial : RuntimePrimitiveFactory.DefaultMaterial;
            mantle.GetPropertyBlock(lampProperties);
            Color colour = lit ? new Color(1f, .81f, .5f) : new Color(.46f, .43f, .34f);
            lampProperties.SetColor("_BaseColor", colour);
            lampProperties.SetColor("_Color", colour);
            lampProperties.SetColor("_EmissionColor", lit ? colour * 2.4f : Color.black);
            mantle.SetPropertyBlock(lampProperties);
        }

        private Transform Anchor(string name) => LodgeStovePlan.Require(transform, name);
        private void OnEnable() { if (LanternLight != null) RestoreState(); }
        private void OnDisable() { if (LanternLight != null) ApplyLantern(); }
    }
}
