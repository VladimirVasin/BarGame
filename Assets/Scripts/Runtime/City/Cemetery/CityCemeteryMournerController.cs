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

        /// <summary>The laid bouquet mirrors the authored offering
        /// spot across the slab's axis, so a grave that already owns
        /// flowers receives hers beside them, not through them.</summary>
        private static readonly Vector3 LaidBouquetLocalOffset =
            new Vector3(-0.22f, 0.17f, -0.45f);

        private static readonly Color StemColor =
            new Color(0.055f, 0.110f, 0.060f);
        private static readonly Color WrapColor =
            new Color(0.330f, 0.300f, 0.240f);
        private static readonly Color[] BloomColors =
        {
            new Color(0.290f, 0.060f, 0.075f),
            new Color(0.550f, 0.530f, 0.500f),
            new Color(0.220f, 0.100f, 0.300f),
            new Color(0.450f, 0.280f, 0.060f)
        };

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
        private GameObject laidBouquet;

        public bool HasActiveMourner => presentation != null;
        public CemeteryMournerPhase? ActivePhase => timeline?.Phase;

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
                    presentation.HideHeldBouquet();
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
        /// The bouquet she leaves on the slab: a few shared-material
        /// boxes in the palette of the visit, standing where the
        /// cemetery's own authored offerings stand. It lives exactly
        /// as long as her visit, so repeat visits never pile up props.
        /// </summary>
        private GameObject CreateLaidBouquet()
        {
            var root = new GameObject("Cemetery Mourner Bouquet");
            root.transform.SetParent(transform, false);
            root.transform.SetPositionAndRotation(
                grave.Ground + grave.Yaw * LaidBouquetLocalOffset,
                grave.Yaw);

            Material material = null;
            if (presentation != null &&
                presentation.Registry.Renderers.Count > 0 &&
                presentation.Registry.Renderers[0] != null)
            {
                material = presentation.Registry.Renderers[0]
                    .sharedMaterial;
            }

            Color bloom = BloomColors[
                presentation != null
                    ? presentation.Registry.PaletteVariant %
                      BloomColors.Length
                    : 0];
            AddBouquetBox(
                root.transform, material, StemColor,
                new Vector3(0f, 0f, -0.02f),
                new Vector3(0.05f, 0.05f, 0.24f));
            AddBouquetBox(
                root.transform, material, WrapColor,
                new Vector3(0f, 0.005f, 0.02f),
                new Vector3(0.09f, 0.06f, 0.11f));
            AddBouquetBox(
                root.transform, material, bloom,
                new Vector3(0.015f, 0.02f, 0.10f),
                new Vector3(0.12f, 0.08f, 0.11f));
            AddBouquetBox(
                root.transform, material, bloom,
                new Vector3(-0.035f, 0.01f, 0.07f),
                new Vector3(0.08f, 0.06f, 0.08f));
            return root;
        }

        private static void AddBouquetBox(
            Transform parent,
            Material material,
            Color color,
            Vector3 localPosition,
            Vector3 localScale)
        {
            GameObject box = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            box.name = "Bouquet Part";
            Collider collider = box.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = localScale;
            var renderer = box.GetComponent<MeshRenderer>();
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }

            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
            renderer.SetPropertyBlock(properties);
        }

        private void DestroyLaidBouquet()
        {
            if (laidBouquet != null)
            {
                Destroy(laidBouquet);
                laidBouquet = null;
            }
        }
    }
}
