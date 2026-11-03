using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A released empty bottle owns bounded real physics and a single impact decision.</summary>
    [DisallowMultipleComponent]
    public sealed class ThrownVodkaBottle : MonoBehaviour
    {
        public const int MaximumLiveBottles = 4;
        public const float IntactLifetime = 45f;
        public const float BrokenLifetime = 12f;
        private static readonly List<ThrownVodkaBottle> Live = new List<ThrownVodkaBottle>(MaximumLiveBottles);
        private readonly Rigidbody[] shards = new Rigidbody[8];
        private readonly Vector3[] frozenVelocity = new Vector3[9];
        private readonly Vector3[] frozenSpin = new Vector3[9];
        private VodkaBottleView view;
        private Collider[] bottleColliders, heroColliders;
        private Collider vehicleHull;
        private WarehouseFishLampBurstAudio glassAudio;
        private float age, brokenAge, lastContact = -1f;
        private bool paused, released;
        private Vector3 previousWaterPoint;
        private float waterAge;
        public bool IsSubmerged { get; private set; }
        public bool IsBroken { get; private set; }
        public int ImpactCount { get; private set; }
        public FootstepGroundKind LastCanImpactSurface { get; private set; }
        public RetroSfxId LastCanImpactCue { get; private set; }
        public float LastCanMetalVolume { get; private set; }
        public Rigidbody Body { get; private set; }
        public int ActiveShardCount { get { int count = 0; foreach (Rigidbody shard in shards) if (shard != null && shard.gameObject.activeSelf) count++; return count; } }
        public static int ActiveCount => Live.Count;

        public static ThrownVodkaBottle Release(VodkaBottleView bottle, Vector3 velocity,
            Vector3 angularVelocity, Transform ignoredHero, Collider ignoredVehicleHull = null)
        {
            if (bottle == null) throw new ArgumentNullException(nameof(bottle));
            ThrownVodkaBottle existing = bottle.GetComponent<ThrownVodkaBottle>();
            if (existing != null && existing.released) return existing;
            while (Live.Count >= MaximumLiveBottles)
            {
                int index = 0;
                for (int i = 0; i < Live.Count; i++)
                {
                    ThrownVodkaBottle candidate = Live[i];
                    if (candidate == null || candidate.IsBroken || (candidate.Body != null && candidate.Body.IsSleeping()) ||
                        !candidate.IsVisible()) { index = i; break; }
                }
                ThrownVodkaBottle oldest = Live[index]; Live.RemoveAt(index);
                if (oldest != null) { oldest.gameObject.SetActive(false); Destroy(oldest.gameObject); }
            }
            bottle.transform.SetParent(null, true);
            bottle.SetRemaining(0f); bottle.SetOpened(true);
            ThrownVodkaBottle owner = existing != null ? existing : bottle.gameObject.AddComponent<ThrownVodkaBottle>();
            owner.view = bottle;
            owner.vehicleHull = ignoredVehicleHull;
            owner.heroColliders = ignoredHero != null ? ignoredHero.GetComponentsInChildren<Collider>(true) : Array.Empty<Collider>();
            var lower = bottle.gameObject.AddComponent<CapsuleCollider>();
            DrinkContainerProfile profile = bottle.Profile;
            bool vodka = profile.ItemId == InventoryItemId.VodkaBottle;
            lower.direction = 1; lower.center = Vector3.up * (vodka ? VodkaBottleDimensions.LowerColliderCentre : profile.Height * .35f);
            lower.radius = profile.Radius; lower.height = vodka ? VodkaBottleDimensions.LowerColliderHeight : profile.Height * .70f;
            var neck = bottle.gameObject.AddComponent<CapsuleCollider>();
            neck.direction = 1; neck.center = Vector3.up * (vodka ? VodkaBottleDimensions.NeckColliderCentre : profile.Height * .78f);
            neck.radius = profile.IsCan ? profile.Radius : Mathf.Min(.0165f, profile.Radius * .48f); neck.height = vodka ? VodkaBottleDimensions.NeckColliderHeight : profile.Height * .44f;
            owner.bottleColliders = new Collider[] { lower, neck };
            owner.Body = bottle.gameObject.AddComponent<Rigidbody>();
            owner.Body.mass = profile.IsCan ? .016f : profile.ItemId == InventoryItemId.HawthornTincture ? .065f : .38f; owner.Body.centerOfMass = Vector3.up * (vodka ? VodkaBottleDimensions.CentreOfMass : profile.Height * .36f);
            owner.Body.linearDamping = .025f; owner.Body.angularDamping = .12f;
            owner.Body.interpolation = RigidbodyInterpolation.Interpolate;
            owner.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            foreach (Collider collider in owner.bottleColliders) owner.IgnoreHero(collider);
            owner.Body.linearVelocity = velocity; owner.Body.angularVelocity = angularVelocity;
            if (!profile.IsCan)
            {
                owner.glassAudio = bottle.gameObject.AddComponent<WarehouseFishLampBurstAudio>();
                owner.glassAudio.Initialize(bottle.transform);
                owner.glassAudio.Source.minDistance = .6f; owner.glassAudio.Source.maxDistance = 16f;
            }
            owner.previousWaterPoint = owner.Body.worldCenterOfMass;
            owner.released = true; Live.Add(owner);
            owner.SetPaused(GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused || !GameCursor.ApplicationFocused);
            return owner;
        }

        public static bool ShouldBreak(FootstepGroundKind surface, float normalSpeed)
        {
            float threshold = surface is FootstepGroundKind.Soil or FootstepGroundKind.Grass or
                FootstepGroundKind.Sand or FootstepGroundKind.Snow ? 6f :
                surface is FootstepGroundKind.Wood or FootstepGroundKind.Carpet ? 4f : 2.5f;
            return normalSpeed >= threshold;
        }

        public static bool ShouldBreak(InventoryItemId item, FootstepGroundKind surface, float normalSpeed) =>
            !DrinkContainerCatalog.Get(item).IsCan && ShouldBreak(surface, normalSpeed);

        public static RetroSfxId CanImpactSurfaceCue(FootstepGroundKind surface) => surface switch
        {
            FootstepGroundKind.Metal => RetroSfxId.FootstepMetal,
            FootstepGroundKind.Wood => RetroSfxId.FootstepWood,
            FootstepGroundKind.Soil => RetroSfxId.FootstepSoil,
            FootstepGroundKind.Grass => RetroSfxId.FootstepGrass,
            FootstepGroundKind.Sand => RetroSfxId.FootstepSand,
            FootstepGroundKind.Snow => RetroSfxId.FootstepSnow,
            FootstepGroundKind.Carpet => RetroSfxId.FootstepCarpet,
            FootstepGroundKind.Puddle => RetroSfxId.FootstepPuddle,
            FootstepGroundKind.Tile => RetroSfxId.FootstepTile,
            FootstepGroundKind.Stone => RetroSfxId.FootstepStone,
            _ => RetroSfxId.FootstepConcrete
        };

        public static float CanMetalImpactVolume(FootstepGroundKind surface) => surface switch
        {
            FootstepGroundKind.Metal => .85f,
            FootstepGroundKind.Wood => .24f,
            FootstepGroundKind.Soil or FootstepGroundKind.Grass or FootstepGroundKind.Sand or
                FootstepGroundKind.Snow or FootstepGroundKind.Carpet => .065f,
            FootstepGroundKind.Puddle => .12f,
            _ => .52f
        };

        private void OnCollisionEnter(Collision collision)
        {
            if (!released || paused || IsBroken || IsSubmerged || collision == null || collision.contactCount == 0) return;
            ContactPoint contact = collision.GetContact(0);
            float speed = 0f;
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint candidate = collision.GetContact(i);
                float normal = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, candidate.normal));
                if (normal > speed) { speed = normal; contact = candidate; }
            }
            if (speed < .3f) return;
            ImpactCount++;
            FootstepGroundKind kind = ResolveSurface(collision.collider, contact.point, contact.normal);
            if (ShouldBreak(view.ItemId, kind, speed)) { Shatter(contact.point, collision.relativeVelocity); return; }
            if (age - lastContact > .14f && ImpactCount <= 8)
            {
                lastContact = age;
                if (view.Profile.IsCan)
                {
                    // One thin-metal transient plus its struck surface, at most two shared
                    // pooled voices. Soft ground damps the ring; it never substitutes glass.
                    float impact = Mathf.Clamp(speed / 4f, .18f, 1f);
                    LastCanImpactSurface = kind;
                    LastCanImpactCue = CanImpactSurfaceCue(kind);
                    LastCanMetalVolume = CanMetalImpactVolume(kind) * impact;
                    RetroAudio.PlayAt(RetroSfxId.FootstepMetal, contact.point, LastCanMetalVolume);
                    if (LastCanImpactCue != RetroSfxId.FootstepMetal)
                        RetroAudio.PlayAt(LastCanImpactCue, contact.point, .36f * impact);
                }
                else
                {
                    // Quiet intact-glass contact; fracture has its own cue.
                    RetroAudio.PlayAt(RetroSfxId.FootstepStone, contact.point);
                }
            }
        }

        private static FootstepGroundKind ResolveSurface(Collider collider, Vector3 point, Vector3 normal)
        {
            FootstepGround marker = collider != null ? collider.GetComponentInParent<FootstepGround>() : null;
            int triangle = -1;
            if (collider is MeshCollider && collider.Raycast(new Ray(point + normal * .025f, -normal), out RaycastHit hit, .08f))
                triangle = hit.triangleIndex;
            FootstepGroundKind kind = marker != null ? marker.ResolveKind(triangle) : FootstepGroundKind.None;
            // Render-only rugs/snow/soil overlays can soften the actual top surface. A puddle cannot.
            if (normal.y > .5f && FootstepGroundOverlay.TryFind(point, out FootstepGroundKind overlay) &&
                overlay != FootstepGroundKind.Puddle) kind = overlay;
            return kind;
        }

        private void Shatter(Vector3 point, Vector3 impactVelocity)
        {
            if (IsBroken || view.Profile.IsCan) return;
            IsBroken = true; brokenAge = 0f; view.HideNonGlass();
            Vector3 carriedVelocity = Body.linearVelocity, carriedSpin = Body.angularVelocity;
            Body.collisionDetectionMode = CollisionDetectionMode.Discrete; Body.isKinematic = true;
            foreach (Collider collider in bottleColliders) collider.enabled = false;
            for (int i = 0; i < view.GlassCells.Count && i < shards.Length; i++)
            {
                Renderer renderer = view.GlassCells[i];
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Transform cell = renderer.transform;
                cell.SetParent(null, true);
                var collider = cell.gameObject.AddComponent<BoxCollider>();
                collider.center = filter.sharedMesh.bounds.center;
                collider.size = Vector3.Max(filter.sharedMesh.bounds.size, Vector3.one * .0001f);
                Rigidbody shard = shards[i] = cell.gameObject.AddComponent<Rigidbody>();
                shard.mass = .38f / shards.Length; shard.linearDamping = .12f; shard.angularDamping = .22f;
                shard.interpolation = RigidbodyInterpolation.Interpolate;
                shard.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                shard.solverIterations = 4; shard.solverVelocityIterations = 1;
                IgnoreHero(collider);
                for (int j = 0; j < i; j++)
                    if (shards[j] != null) Physics.IgnoreCollision(collider, shards[j].GetComponent<Collider>());
                Vector3 outward = (renderer.bounds.center - point).normalized;
                shard.linearVelocity = carriedVelocity * .45f + outward * .8f + Vector3.up * .25f;
                shard.angularVelocity = carriedSpin + new Vector3(i % 2 == 0 ? 3f : -3f, 2f, i - 3f);
            }
            glassAudio.Source.transform.position = point;
            glassAudio.PlayBurst(); glassAudio.Source.volume = .38f; glassAudio.Source.pitch = 1.15f;
        }
        private void IgnoreHero(Collider collider)
        {
            // The cabin's finite floor/walls stay solid; only the vehicle's
            // broad outside obstacle box is excluded for this released item.
            if (vehicleHull != null) Physics.IgnoreCollision(collider, vehicleHull);
            foreach (Collider hero in heroColliders) if (hero != null) Physics.IgnoreCollision(collider, hero);
        }
        private void Update()
        {
            if (!released) return;
            SetPaused(GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused || !GameCursor.ApplicationFocused);
            if (paused) return;
            age += Time.deltaTime;
            if (IsBroken) brokenAge += Time.deltaTime;
            if (IsSubmerged) { waterAge += Time.deltaTime; view.StepWaterRipple(waterAge); }
            if (age >= IntactLifetime || (IsBroken && brokenAge >= BrokenLifetime)) Destroy(gameObject);
            if (transform.position.y < -100f) Destroy(gameObject);
        }
        private bool IsVisible()
        {
            if (view == null) return false;
            return view.BodyIsVisible;
        }
        private void FixedUpdate()
        {
            if (!released || paused || IsBroken || Body == null) return;
            Vector3 current = Body.worldCenterOfMass;
            if (!IsSubmerged && WorldWaterSurface.TryCrossing(previousWaterPoint, current, out Vector3 contact))
            {
                IsSubmerged = true;
                Body.linearVelocity *= .35f; Body.angularVelocity *= .4f;
                Body.linearDamping = 2f; Body.angularDamping = 1.5f;
                RetroAudio.PlayAt(RetroSfxId.FootstepPuddle, contact);
                view.BeginWaterRipple(contact);
            }
            if (IsSubmerged) Body.AddForce(-Physics.gravity * .82f, ForceMode.Acceleration);
            previousWaterPoint = current;
        }
        private void SetPaused(bool value)
        {
            if (value == paused) return;
            paused = value;
            glassAudio?.SetPaused(value);
            Freeze(Body, 0, value, !IsBroken);
            for (int i = 0; i < shards.Length; i++) Freeze(shards[i], i + 1, value, true);
        }
        private void Freeze(Rigidbody body, int slot, bool value, bool active)
        {
            if (body == null || !active) return;
            if (value)
            {
                frozenVelocity[slot] = body.linearVelocity; frozenSpin[slot] = body.angularVelocity;
                body.collisionDetectionMode = CollisionDetectionMode.Discrete; body.isKinematic = true;
            }
            else
            {
                body.isKinematic = false; body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.linearVelocity = frozenVelocity[slot]; body.angularVelocity = frozenSpin[slot];
            }
        }
        private void OnDisable()
        {
            Live.Remove(this);
            if (Body != null) { Body.collisionDetectionMode = CollisionDetectionMode.Discrete; Body.isKinematic = true; }
            foreach (Rigidbody shard in shards) if (shard != null) shard.gameObject.SetActive(false);
            glassAudio?.Stop();
        }
        private void OnDestroy()
        {
            Live.Remove(this);
            foreach (Rigidbody shard in shards) if (shard != null) Destroy(shard.gameObject);
        }
        public static void ResetForTests()
        {
            foreach (ThrownVodkaBottle bottle in Live.ToArray())
                if (bottle != null) DestroyImmediate(bottle.gameObject);
            Live.Clear();
        }
        public static void ClearForNewSession()
        {
            while (Live.Count > 0)
            {
                ThrownVodkaBottle bottle = Live[Live.Count - 1];
                Live.RemoveAt(Live.Count - 1);
                if (bottle == null) continue;
                bottle.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(bottle.gameObject);
                else DestroyImmediate(bottle.gameObject);
            }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Live.Clear();
    }
}
