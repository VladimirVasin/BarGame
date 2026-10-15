using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public sealed partial class WarehouseFishLamp
    {
        public const float HeatingSeconds = WarehouseFishLampState.HeatingSeconds;
        private const float SettleSeconds = 3f, DepartureDistance = 6.5f;
        private static GameObject shardTemplate;
        private readonly Rigidbody[] shardBodies = new Rigidbody[8];
        private readonly Collider[] shardColliders = new Collider[8];
        private readonly Vector3[] shardRest = new Vector3[8], pausedVelocity = new Vector3[8], pausedSpin = new Vector3[8];
        private readonly Quaternion[] shardRotations = new Quaternion[8];
        private WarehouseFishLampBurstAudio burstAudio;
        private Transform warehouseLighting;
        private Light[] practicals = Array.Empty<Light>();
        private bool[] practicalEnabled = Array.Empty<bool>();
        private Renderer[] lenses = Array.Empty<Renderer>();
        private MaterialPropertyBlock[] lensRest = Array.Empty<MaterialPropertyBlock>();
        private GameObject shards;
        private Vector3 previousFeet;
        private bool hasPreviousFeet, blackoutApplied, fragmentsReleased, fragmentsPaused;
        private float baseRange, fragmentElapsed, downwardTravel;
        private WarehouseFishLampState Failure => GameSessionState.WarehouseFishLamp;
        public WarehouseFishLampPhase Phase => Failure.Phase;
        public float HeatProgress => Failure.Heat01;
        public float HeatElapsed => Failure.HeatingElapsed;
        public IReadOnlyList<Rigidbody> ShardBodies => shardBodies;
        public int HeatingPlayCount => burstAudio != null ? burstAudio.HeatingPlayCount : 0;
        public int BurstPlayCount => burstAudio != null ? burstAudio.BurstPlayCount : 0;
        public AudioSource CueSource => burstAudio != null ? burstAudio.Source : null;

        private void InitializeBurst(GameObject fixture)
        {
            baseRange = Light.range;
            burstAudio = fixture.AddComponent<WarehouseFishLampBurstAudio>();
            burstAudio.Initialize(Light.transform);
            if (shardTemplate == null) shardTemplate = Resources.Load<GameObject>("CombatWarehouse/FishLampBurst/LampShards");
            if (shardTemplate == null) throw new InvalidOperationException("Missing authored fish lamp shards.");
            shards = new GameObject("Warehouse Lamp Shards");
            shards.transform.SetParent(fixture.transform, false);
            Instantiate(shardTemplate, shards.transform, false);
            MeshRenderer[] pieces = shards.GetComponentsInChildren<MeshRenderer>(true);
            Array.Sort(pieces, (a, b) => string.CompareOrdinal(a.name, b.name));
            if (pieces.Length != 8) throw new InvalidOperationException("The pendant needs eight authored glass fragments.");
            var colliders = new Collider[8];
            Material glass = WarehouseArenaAssetProvider.Surface("FishStationLamp", "Glass");
            for (int i = 0; i < pieces.Length; i++)
            {
                MeshRenderer piece = pieces[i];
                piece.sharedMaterial = glass;
                piece.shadowCastingMode = ShadowCastingMode.Off;
                piece.reflectionProbeUsage = ReflectionProbeUsage.Off;
                var pivot = new GameObject("Warehouse Bulb " + piece.name).transform;
                pivot.SetParent(shards.transform, false);
                pivot.SetPositionAndRotation(piece.bounds.center, fixture.transform.rotation);
                piece.transform.SetParent(pivot, true);
                Rigidbody body = pivot.gameObject.AddComponent<Rigidbody>();
                body.isKinematic = true; body.useGravity = true; body.mass = .004f;
                body.linearDamping = .18f; body.angularDamping = .25f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.solverIterations = 6; body.solverVelocityIterations = 2;
                var collider = piece.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = piece.GetComponent<MeshFilter>().sharedMesh;
                collider.convex = true;
                colliders[i] = collider; shardColliders[i] = collider; shardBodies[i] = body;
                shardRest[i] = pivot.localPosition; shardRotations[i] = pivot.localRotation;
            }
            for (int a = 0; a < colliders.Length; a++)
                for (int b = a + 1; b < colliders.Length; b++) Physics.IgnoreCollision(colliders[a], colliders[b]);
            shards.SetActive(false);
        }

        internal void BindWarehouseLighting(Transform warehouse)
        {
            warehouseLighting = warehouse;
            // The legacy player proxy covers the high faucet. Glass, like the fish sections,
            // uses the authored trough/rims beneath it instead of that invisible envelope.
            foreach (Collider collider in warehouse.GetComponentsInChildren<Collider>(true))
                if (collider.name.StartsWith("Collision_PreparationStation", StringComparison.Ordinal))
                    foreach (Collider fragment in shardColliders) Physics.IgnoreCollision(fragment, collider);
            practicals = warehouse.GetComponentsInChildren<Light>(true);
            practicalEnabled = new bool[practicals.Length];
            for (int i = 0; i < practicals.Length; i++) practicalEnabled[i] = practicals[i].enabled;
            var emissive = new List<Renderer>(24);
            foreach (Renderer renderer in warehouse.GetComponentsInChildren<Renderer>(true))
                if (renderer.name.Contains("__Lamp") || renderer.name.Contains("__SignalRed")) emissive.Add(renderer);
            lenses = emissive.ToArray(); lensRest = new MaterialPropertyBlock[lenses.Length];
            for (int i = 0; i < lenses.Length; i++)
            { lensRest[i] = new MaterialPropertyBlock(); lenses[i].GetPropertyBlock(lensRest[i]); }
            if (Phase == WarehouseFishLampPhase.Burst) ApplyWarehouseBlackout();
        }

        private bool AdvanceBurst(float seconds)
        {
            Vector3 feet = owner.Hero != null ? owner.Hero.transform.position - Vector3.up * PlayerFactory.GroundedRootOffset : default;
            bool keyReceived = GameSessionState.WarehouseFishAssembly.RewardGranted && !GameSessionState.WarehouseFishAssembly.ReceiptPending;
            if (!hasPreviousFeet || !keyReceived || (previousFeet - feet).sqrMagnitude >= .8f * .8f) downwardTravel = 0f;
            else downwardTravel = Mathf.Max(0f, downwardTravel + previousFeet.y - feet.y);
            bool downward = downwardTravel > .04f;
            previousFeet = feet; hasPreviousFeet = owner.Hero != null;
            if (Phase == WarehouseFishLampPhase.Waiting)
            {
                if (IsLit && !BarMinigameModalLock.IsAnyLocked && owner.IsInitialized &&
                    Failure.TryBegin(keyReceived,
                        downward && IsUpperStairDeparture(feet)))
                {
                    Source.Stop();
                    burstAudio.PlayHeating(0f, true);
                    ApplyRetainedPhase();
                    return true;
                }
                return false;
            }
            if (Phase == WarehouseFishLampPhase.Heating)
            {
                Failure.Advance(seconds);
                if (Phase == WarehouseFishLampPhase.Burst) Burst(true);
                else ApplyRetainedPhase();
                return true;
            }
            if (fragmentsReleased && fragmentElapsed < SettleSeconds)
            {
                fragmentElapsed = Mathf.Min(SettleSeconds, fragmentElapsed + seconds);
                if (fragmentElapsed >= SettleSeconds)
                    foreach (Rigidbody body in shardBodies) body.isKinematic = true;
            }
            return true;
        }

        private bool IsUpperStairDeparture(Vector3 feet)
        {
            Vector2 distance = new Vector2(feet.x - transform.position.x, feet.z - transform.position.z);
            if (distance.sqrMagnitude < DepartureDistance * DepartureDistance || feet.y >= WarehouseArenaPlan.TopHeight - .07f) return false;
            foreach (WarehouseArenaStair stair in owner.WarehousePlan.Stairs)
            {
                if (Mathf.Abs(stair.Top.y - WarehouseArenaPlan.TopHeight) > .01f) continue;
                Rect bounds = stair.Bounds;
                if (feet.x > bounds.xMin && feet.x < bounds.xMax && feet.z > bounds.yMin && feet.z < bounds.yMax - .04f &&
                    Mathf.Abs(feet.y - stair.HeightAt(feet.z)) < .23f) return true;
            }
            return false;
        }

        private void ApplyRetainedPhase()
        {
            if (Phase == WarehouseFishLampPhase.Burst) { Burst(false); return; }
            bulb.enabled = true;
            if (fragmentsReleased)
            {
                fragmentsReleased = fragmentsPaused = false; shards.SetActive(false);
                for (int i = 0; i < shardBodies.Length; i++)
                {
                    shardBodies[i].isKinematic = true;
                    shardBodies[i].transform.SetLocalPositionAndRotation(shardRest[i], shardRotations[i]);
                }
            }
            float heat = Phase == WarehouseFishLampPhase.Heating ? Mathf.SmoothStep(0f, 1f, HeatProgress) : 0f;
            Light.color = Phase == WarehouseFishLampPhase.Heating ? Color.Lerp(Color.white, new Color(1f, .88f, .66f), heat) : Color.white;
            Light.range = Mathf.Lerp(baseRange, Mathf.Max(18f, baseRange), heat);
            ApplyFactor(Phase == WarehouseFishLampPhase.Heating ? Mathf.Lerp(1f, 9f, heat) :
                IsLit ? HomeBathroomLightFlicker.EvaluateFactor(elapsed) : 0f, false);
        }

        private void Burst(bool play)
        {
            IsLit = false; Source.Stop(); ApplyFactor(0f, false); bulb.enabled = false;
            if (play && !fragmentsReleased)
            {
                fragmentsReleased = true; fragmentsPaused = false; fragmentElapsed = 0f; shards.SetActive(true);
                Vector3 centre = Light.transform.position;
                for (int i = 0; i < shardBodies.Length; i++)
                {
                    Rigidbody body = shardBodies[i]; body.isKinematic = false;
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                    Vector3 radial = Vector3.ProjectOnPlane(body.position - centre, Vector3.up).normalized;
                    body.linearVelocity = radial * (.7f + .08f * (i % 3)) + Vector3.down * .45f;
                    body.angularVelocity = new Vector3(i % 2 == 0 ? 11f : -11f, 7f, i % 3 == 0 ? 9f : -9f);
                }
                burstAudio.PlayBurst();
            }
            ApplyWarehouseBlackout();
        }

        private void ApplyWarehouseBlackout()
        {
            if (blackoutApplied || warehouseLighting == null) return;
            blackoutApplied = true;
            foreach (Light practical in practicals) if (practical != null) practical.enabled = false;
            for (int i = 0; i < lenses.Length; i++)
            {
                lenses[i].GetPropertyBlock(properties);
                properties.SetColor(EmissionColorId, Color.black);
                properties.SetColor(BaseColorId, new Color(.025f, .025f, .025f));
                lenses[i].SetPropertyBlock(properties);
            }
            WarehouseArenaLighting.SetBlackout(warehouseLighting, true);
        }

        private void RestoreWarehouseLighting()
        {
            if (!blackoutApplied) return;
            blackoutApplied = false;
            for (int i = 0; i < practicals.Length; i++) if (practicals[i] != null) practicals[i].enabled = practicalEnabled[i];
            for (int i = 0; i < lenses.Length; i++) if (lenses[i] != null) lenses[i].SetPropertyBlock(lensRest[i]);
            WarehouseArenaLighting.SetBlackout(warehouseLighting, false);
            burstAudio.Stop();
        }

        private void SuspendBurst()
        {
            burstAudio.Stop();
            if (!fragmentsReleased || fragmentElapsed >= SettleSeconds) return;
            fragmentsPaused = true;
            for (int i = 0; i < shardBodies.Length; i++)
            {
                pausedVelocity[i] = shardBodies[i].linearVelocity; pausedSpin[i] = shardBodies[i].angularVelocity;
                shardBodies[i].isKinematic = true;
            }
        }

        private void ResumeBurst()
        {
            if (Phase == WarehouseFishLampPhase.Heating) burstAudio.PlayHeating(HeatElapsed, false);
            if (!fragmentsPaused || Phase != WarehouseFishLampPhase.Burst) return;
            fragmentsPaused = false;
            for (int i = 0; i < shardBodies.Length; i++)
            {
                shardBodies[i].isKinematic = false;
                shardBodies[i].linearVelocity = pausedVelocity[i]; shardBodies[i].angularVelocity = pausedSpin[i];
            }
        }

        private void ReleaseBlackout()
        { if (warehouseLighting != null) WarehouseArenaLighting.SetBlackout(warehouseLighting, false); }
    }
}
