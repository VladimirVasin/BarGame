using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The cemetery's one scripted visitor. While the hero is near the
    /// grounds and the cooldown has passed, a woman in deep mourning
    /// spawns on the street out of sight — the pedestrian director's
    /// spawn rules, applied by hand — walks in through the gate with a
    /// bouquet clasped to her chest, lays it on a deterministic random
    /// grave, cries for exactly thirty seconds, wipes her eyes and
    /// walks out, taking her presentation with her. Owned by the City
    /// root and polled like the weighbridge needle; the staged prefab
    /// itself stays passive. She finishes the rite even when the hero
    /// wanders off — only the director's despawn distance, out of
    /// camera, removes her early.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityCemeteryMournerController : MonoBehaviour
    {
        public const string RuntimeObjectName =
            "Cemetery Mourner Controller";

        /// <summary>A grieving pace: slower than any commuting walker.</summary>
        public const float WalkSpeedMetersPerSecond = 1.05f;

        /// <summary>How long after one visit before the next mourner
        /// may answer the hero's presence.</summary>
        public const float CooldownSeconds = 180f;

        /// <summary>Mirrors CityPedestrianDirector.DespawnDistance —
        /// past this, and unseen, an unfinished rite ends quietly.</summary>
        public const float DespawnDistance = 88f;

        public const float MaximumStepSeconds = 0.1f;
        private const float TurnDegreesPerSecond = 220f;

        /// <summary>
        /// Where on the slab the laid bouquet is centred, in the grave's
        /// frame: it mirrors the authored offering spot across the slab's
        /// axis, so a grave that already owns flowers receives hers beside
        /// them, not through them. Planar only — the height is the slab
        /// top the anchor carries, and the prop is rested on it by
        /// <see cref="CemeteryLaidBouquet"/> rather than by a guessed
        /// offset.
        /// </summary>
        public static readonly Vector3 LaidBouquetLocalOffset =
            new Vector3(-0.22f, 0f, -0.45f);

        private CityLayout layout;
        private CityCemeteryPlan cemeteryPlan;
        private CityOpenAreaAccessDescriptor access;
        private List<CemeteryGraveAnchor> candidates;
        private Transform player;
        private Transform cameraTransform;
        private CemeteryMournerProvider provider;
        private int citySeed;

        private int visitIndex;
        private float cooldownRemaining;

        private CemeteryMournerPresentation presentation;
        private CemeteryMournerTimeline timeline;
        private Vector3[] approachRoute;
        private Vector3[] departRoute;
        private float travelledDistance;
        private Vector3 standPoint;
        private Vector3 standFacing;
        private CemeteryGraveAnchor grave;
        private CityPedestrianHandPropRegistry laidBouquet;

        public bool HasActiveMourner => presentation != null;
        public CemeteryMournerPhase? ActivePhase => timeline?.Phase;

        /// <summary>The bouquet lying on the grave during the current
        /// visit, null before the lay cue and after the visit.</summary>
        public CityPedestrianHandPropRegistry LaidBouquet => laidBouquet;

        /// <summary>The grave of the current visit (meaningful only while
        /// <see cref="HasActiveMourner"/>).</summary>
        public CemeteryGraveAnchor ActiveGrave => grave;

        public static CityCemeteryMournerController Create(
            Transform parent,
            CityLayout layout,
            CityCemeteryPlan cemeteryPlan,
            Transform player,
            Camera camera,
            int citySeed)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (player == null)
            {
                throw new ArgumentNullException(nameof(player));
            }

            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            // A custom blueprint without a cemetery simply has no
            // mourner, the same silent absence as its graves.
            if (cemeteryPlan == null ||
                !CemeteryMournerPlan.TryGetAccess(
                    layout,
                    out CityOpenAreaAccessDescriptor access))
            {
                return null;
            }

            List<CemeteryGraveAnchor> candidates =
                CemeteryMournerPlan.CollectCandidateGraves(cemeteryPlan);
            if (candidates.Count == 0)
            {
                return null;
            }

            CemeteryMournerProvider provider =
                CemeteryMournerProvider.Load();
            if (provider == null || provider.StagedPrefab == null)
            {
                GameLog.Warning(
                    "city",
                    "cemetery_mourner_provider_missing");
                return null;
            }

            var controller = new GameObject(RuntimeObjectName)
                .AddComponent<CityCemeteryMournerController>();
            controller.transform.SetParent(parent, false);
            controller.layout = layout;
            controller.cemeteryPlan = cemeteryPlan;
            controller.access = access;
            controller.candidates = candidates;
            controller.player = player;
            controller.cameraTransform = camera.transform;
            controller.provider = provider;
            controller.citySeed = citySeed;
            return controller;
        }

        private void Update()
        {
            if (player == null)
            {
                return;
            }

            if (presentation != null)
            {
                AdvanceVisit(
                    Mathf.Min(Time.deltaTime, MaximumStepSeconds));
                return;
            }

            if (cooldownRemaining > 0f)
            {
                cooldownRemaining -= Time.deltaTime;
                return;
            }

            if (CemeteryMournerPlan.IsInsideTriggerBand(
                    cemeteryPlan.Grounds,
                    new Vector2(
                        player.position.x,
                        player.position.z)))
            {
                StartVisit();
            }
        }

        private void OnDestroy()
        {
            if (presentation != null)
            {
                Destroy(presentation.gameObject);
                presentation = null;
            }

            DestroyLaidBouquet();
        }

        private void StartVisit()
        {
            uint randomState = CemeteryMournerPlan.CreateVisitRandomState(
                citySeed,
                visitIndex);
            visitIndex++;

            grave = candidates[CemeteryMournerPlan.SelectGraveIndex(
                candidates.Count,
                ref randomState)];
            int paletteVariant = (int)(
                CemeteryMournerPlan.NextRandomState(ref randomState) %
                4u);
            standPoint = CemeteryMournerPlan.ComputeStandPoint(
                grave,
                cemeteryPlan.GroundTopY);
            standFacing = CemeteryMournerPlan.ComputeStandFacing(grave);

            Vector3 spawnPoint = CemeteryMournerPlan.SelectSpawnPoint(
                access,
                player.position,
                cameraTransform.position,
                cameraTransform.forward);
            if (CityTerrainSurfacePlan.TrySampleGroundTop(
                    layout,
                    new Vector2(spawnPoint.x, spawnPoint.z),
                    out float spawnGround,
                    out _))
            {
                spawnPoint.y = spawnGround;
            }

            approachRoute = CemeteryMournerPlan.BuildApproachRoute(
                layout,
                access,
                cemeteryPlan,
                spawnPoint,
                standPoint);
            departRoute = CemeteryMournerPlan.ReverseRoute(approachRoute);
            timeline = new CemeteryMournerTimeline(
                CemeteryMournerPlan.ComputeRouteLength(approachRoute) /
                WalkSpeedMetersPerSecond,
                CemeteryMournerPlan.ComputeRouteLength(departRoute) /
                WalkSpeedMetersPerSecond);
            travelledDistance = 0f;

            CemeteryMournerPlan.EvaluateRoute(
                approachRoute,
                0.1f,
                out Vector3 initialDirection);
            presentation = CemeteryMournerFactory.Create(
                transform,
                spawnPoint,
                initialDirection,
                paletteVariant,
                provider);
            if (presentation == null)
            {
                timeline = null;
                cooldownRemaining = CooldownSeconds;
            }
        }

        private void AdvanceVisit(float deltaTime)
        {
            CemeteryMournerPhase phaseBefore = timeline.Phase;
            timeline.Advance(deltaTime);
            if (timeline.Phase != phaseBefore)
            {
                OnPhaseEntered(timeline.Phase);
            }

            if (timeline.IsDone)
            {
                FinishVisit(true);
                return;
            }

            if (timeline.IsWalkingPhase)
            {
                travelledDistance +=
                    WalkSpeedMetersPerSecond * deltaTime;
                Vector3[] route =
                    timeline.Phase == CemeteryMournerPhase.Approach
                        ? approachRoute
                        : departRoute;
                presentation.transform.position =
                    CemeteryMournerPlan.EvaluateRoute(
                        route,
                        travelledDistance,
                        out Vector3 direction);
                RotateToward(direction, deltaTime);
            }
            else
            {
                presentation.transform.position = standPoint;
                RotateToward(standFacing, deltaTime);
                if (timeline.ConsumeLayCue())
                {
                    presentation.ReleaseHeldBouquet();
                    laidBouquet = CreateLaidBouquet();
                }
            }

            // The safety net of the director's despawn band: far away
            // and unseen, an unfinished rite ends without witnesses.
            Vector3 toPlayer =
                presentation.transform.position - player.position;
            toPlayer.y = 0f;
            if (toPlayer.magnitude > DespawnDistance &&
                !IsInFrontOfCamera(presentation.transform.position))
            {
                FinishVisit(false);
            }
        }

        private void OnPhaseEntered(CemeteryMournerPhase phase)
        {
            switch (phase)
            {
                case CemeteryMournerPhase.LayFlowers:
                    // The rite is one authored clip played once; the
                    // walk residual is dropped so pose sections and
                    // timeline phases stay aligned to the frame.
                    presentation.transform.position = standPoint;
                    presentation.PlayMournRite();
                    break;
                case CemeteryMournerPhase.Depart:
                    travelledDistance = 0f;
                    presentation.PlayWalk();
                    break;
            }
        }

        private void FinishVisit(bool completed)
        {
            GameLog.Info(
                "city",
                "cemetery_mourner_departed",
                GameLog.Field("grave_ordinal", grave.Ordinal),
                GameLog.Field("completed", completed));
            if (presentation != null)
            {
                Destroy(presentation.gameObject);
                presentation = null;
            }

            DestroyLaidBouquet();
            timeline = null;
            approachRoute = null;
            departRoute = null;
            cooldownRemaining = CooldownSeconds;
        }

        private void RotateToward(Vector3 direction, float deltaTime)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude < 0.0001f)
            {
                return;
            }

            presentation.transform.rotation = Quaternion.RotateTowards(
                presentation.transform.rotation,
                Quaternion.LookRotation(flat.normalized, Vector3.up),
                TurnDegreesPerSecond * deltaTime);
        }

        private bool IsInFrontOfCamera(Vector3 point)
        {
            Vector3 toPoint = point - cameraTransform.position;
            toPoint.y = 0f;
            Vector3 forward = cameraTransform.forward;
            forward.y = 0f;
            if (toPoint.sqrMagnitude < 0.0001f ||
                forward.sqrMagnitude < 0.0001f)
            {
                return true;
            }

            return Vector3.Dot(
                       toPoint.normalized,
                       forward.normalized) >=
                   CemeteryMournerPlan.SpawnViewCosine;
        }

        /// <summary>
        /// The bouquet she leaves on the slab: the same funeral-bouquet
        /// hand prop she carried in, placed free-standing in the material
        /// and palette of the visit where the cemetery's own authored
        /// offerings stand. It lives exactly as long as her visit, so
        /// repeat visits never pile up props.
        /// </summary>
        private CityPedestrianHandPropRegistry CreateLaidBouquet()
        {
            Material material = null;
            int paletteVariant = 0;
            if (presentation != null)
            {
                paletteVariant = presentation.Registry.PaletteVariant;
                for (int index = 0;
                     index < presentation.Registry.Renderers.Count &&
                     material == null;
                     index++)
                {
                    Renderer renderer =
                        presentation.Registry.Renderers[index];
                    if (renderer != null)
                    {
                        material = renderer.sharedMaterial;
                    }
                }
            }

            return CemeteryLaidBouquet.Place(
                transform,
                ComputeLaidBouquetSlabPoint(grave),
                grave.Yaw,
                material,
                paletteVariant);
        }

        /// <summary>The world point on the slab top the laid bouquet is
        /// centred on. Pure, so a test can pin it.</summary>
        public static Vector3 ComputeLaidBouquetSlabPoint(
            CemeteryGraveAnchor anchor)
        {
            Vector3 point = anchor.Ground + anchor.Yaw * LaidBouquetLocalOffset;
            point.y = anchor.SlabTopY;
            return point;
        }

        private void DestroyLaidBouquet()
        {
            CityPedestrianHandProps.Detach(ref laidBouquet);
        }
    }
}
