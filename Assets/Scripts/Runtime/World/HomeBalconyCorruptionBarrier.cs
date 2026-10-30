using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The existing household leaf becomes a fitted, solid final-view barrier.</summary>
    [DisallowMultipleComponent]
    public sealed class HomeBalconyCorruptionBarrier : MonoBehaviour
    {
        public const float ClosedWidth = 1.35f;
        public const float ClosedHeight = 2.33f;
        private const float SafetyMargin = .14f;
        private const float RetrySeconds = .10f;
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseTransformId = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private Transform pivot, room, player;
        private CharacterController capsule;
        private BoxCollider solidLeaf;
        private Quaternion originalRotation;
        private Vector3 originalScale;
        private float originalWidth, originalHeight, nextRetry;
        private int desiredDay = HomeApartmentDayRules.FirstDayNumber;
        private Surface[] surfaces;
        private MaterialPropertyBlock scratch;

        private sealed class Surface
        {
            internal Renderer Renderer;
            internal Material Material;
            internal MaterialPropertyBlock Original;
            internal bool Glass;
            internal HomeSurfaceKind Kind;
            internal SurfaceDamageLibrary.Entry Damage;
        }

        public bool IsInitialized => surfaces != null;
        public bool IsSealed { get; private set; }
        public bool IsSealPending { get; private set; }
        public Transform DoorPivot => pivot;
        public BoxCollider DoorCollider => solidLeaf;

        internal void Initialize(Transform leafPivot, Renderer[] frame, Vector3 openingSize)
        {
            if (leafPivot == null || frame == null || frame.Length != 3 || leafPivot.parent == null)
                throw new ArgumentException("The balcony barrier requires its existing leaf and measured frame.");
            pivot = leafPivot;
            scratch = new MaterialPropertyBlock();
            room = transform.parent;
            originalRotation = pivot.localRotation;
            originalScale = pivot.localScale;
            originalWidth = openingSize.z - .12f;
            originalHeight = openingSize.y - .08f;
            // This is a collision proxy for the assembled imported leaf. It
            // adds no rendered geometry and always follows that same leaf.
            solidLeaf = pivot.gameObject.AddComponent<BoxCollider>();
            solidLeaf.center = new Vector3(0f, originalHeight * .5f, originalWidth * .5f);
            solidLeaf.size = new Vector3(.10f, originalHeight, originalWidth);
            solidLeaf.enabled = false;
            var cache = new List<Surface>(11);
            Cache(cache, frame[0], "frame-south");
            Cache(cache, frame[1], "frame-north");
            Cache(cache, frame[2], "header");
            int stile = 0;
            foreach (Renderer renderer in pivot.GetComponentsInChildren<Renderer>(true))
            {
                string suffix;
                switch (renderer.name)
                {
                    case "Home Balcony Door Leaf Stile": suffix = stile++ == 0 ? "hinge-stile" : "latch-stile"; break;
                    case "Home Balcony Door Leaf Bottom Rail": suffix = "bottom-rail"; break;
                    case "Home Balcony Door Leaf Middle Rail": suffix = "middle-rail"; break;
                    case "Home Balcony Door Leaf Top Rail": suffix = "top-rail"; break;
                    case "Home Balcony Door Lower Panel": suffix = "lower-panel"; break;
                    case "Home Balcony Door Handle": suffix = "handle"; break;
                    case "Home Balcony Door Glass": suffix = "glass"; break;
                    default: throw new InvalidOperationException("Unexpected balcony leaf part: " + renderer.name);
                }
                Cache(cache, renderer, suffix);
            }
            if (cache.Count != 11 || stile != 2)
                throw new InvalidOperationException("The balcony barrier lost an assembled door surface.");
            surfaces = cache.ToArray();
        }

        private static void Cache(List<Surface> cache, Renderer renderer, string suffix)
        {
            if (renderer == null) throw new InvalidOperationException("Missing balcony door surface.");
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            bool glass = suffix == "glass";
            HomeSurfaceKind kind = suffix == "handle" ? HomeSurfaceKind.PaintedMetal : HomeSurfaceKind.RoomWood;
            cache.Add(new Surface { Renderer = renderer, Material = renderer.sharedMaterial, Original = properties,
                Glass = glass, Kind = kind, Damage = glass ? null : SurfaceDamageLibrary.Resolve(
                    "home", null, "barrier:balcony:" + suffix, kind.ToString()) });
        }

        public void BindPlayer(PlayerRuntime runtime)
        {
            if (runtime.GameObject == null) throw new ArgumentException("The balcony barrier needs the real hero.");
            player = runtime.GameObject.transform;
            capsule = runtime.GameObject.GetComponent<CharacterController>();
            if (capsule == null) throw new InvalidOperationException("The balcony barrier needs the real hero capsule.");
            RefreshAppearance(desiredDay);
        }

        public void RefreshAppearance(int dayNumber)
        {
            desiredDay = HomeApartmentDayRules.ResolveDay(dayNumber);
            if (!IsInitialized || !isActiveAndEnabled) return;
            if (desiredDay < HomeApartmentDayRules.LastDayNumber)
            {
                RestoreOrdinary();
                return;
            }
            if (IsSealed) return;
            // A debug appearance change while outdoors leaves the return path
            // open. Close only after the actor is fully inside the safe plane.
            IsSealPending = true;
            if (ActorClearsDoor()) Seal();
        }

        private bool ActorClearsDoor()
        {
            if (player == null || capsule == null || room == null) return false;
            Vector3 centre = room.InverseTransformPoint(player.TransformPoint(capsule.center));
            float radius = capsule.radius * Mathf.Max(Mathf.Abs(player.lossyScale.x), Mathf.Abs(player.lossyScale.z));
            float plane = room.InverseTransformPoint(pivot.position).x;
            float roomScale = Mathf.Max(.001f, Mathf.Abs(room.lossyScale.x));
            return centre.x <= plane - (radius + SafetyMargin) / roomScale;
        }

        private void Update()
        {
            if (!IsSealPending || GameTimeScaleRuntime.IsPaused || SceneTransitionService.IsTransitioning || Time.time < nextRetry) return;
            nextRetry = Time.time + RetrySeconds;
            if (ActorClearsDoor()) Seal();
        }

        private void Seal()
        {
            pivot.localRotation = Quaternion.identity;
            // The old door has generous ajar clearances. Swollen final-view
            // wood seats within its existing jamb/header, leaving1cm reveals.
            pivot.localScale = Vector3.Scale(originalScale,
                new Vector3(1f, ClosedHeight / originalHeight, ClosedWidth / originalWidth));
            foreach (Surface surface in surfaces)
            {
                if (surface.Renderer == null) continue;
                scratch.Clear();
                surface.Renderer.GetPropertyBlock(scratch);
                if (surface.Glass)
                {
                    scratch.SetColor(BaseColorId, new Color(.035f, .043f, .030f, .97f));
                }
                else
                {
                    HomeSurfaceRecipe recipe = HomeSurfaceAppearance.GetRecipe(surface.Kind);
                    Color tint = HomeSurfaceAppearance.CreateDisplayTint(surface.Kind == HomeSurfaceKind.RoomWood
                        ? new Color(.22f, .235f, .17f) : new Color(.18f, .14f, .085f), surface.Kind);
                    scratch.SetTexture(BaseMapId, HomeSurfaceAppearance.GetTexture(surface.Kind));
                    scratch.SetColor(BaseColorId, tint);
                    scratch.SetColor(ColorId, tint);
                    scratch.SetFloat(SmoothnessId, recipe.Smoothness);
                    scratch.SetFloat(MetallicId, recipe.Metallic);
                    SurfaceDamageLibrary.Apply(surface.Renderer, scratch, surface.Damage, room, 1f, surface.Material);
                }
                surface.Renderer.SetPropertyBlock(scratch);
            }
            solidLeaf.enabled = true;
            IsSealed = true;
            IsSealPending = false;
            Physics.SyncTransforms();
        }

        private void RestoreOrdinary()
        {
            if (!IsInitialized || pivot == null || solidLeaf == null) return;
            solidLeaf.enabled = false;
            pivot.localRotation = originalRotation;
            pivot.localScale = originalScale;
            if (IsSealed)
                foreach (Surface surface in surfaces)
                {
                    if (surface.Renderer == null) continue;
                    scratch.Clear();
                    surface.Renderer.GetPropertyBlock(scratch);
                    scratch.SetColor(BaseColorId, surface.Original.GetColor(BaseColorId));
                    if (!surface.Glass)
                    {
                        scratch.SetColor(ColorId, surface.Original.GetColor(ColorId));
                        scratch.SetTexture(BaseMapId, surface.Original.GetTexture(BaseMapId));
                        scratch.SetVector(BaseTransformId, surface.Original.GetVector(BaseTransformId));
                        scratch.SetFloat(SmoothnessId, surface.Original.GetFloat(SmoothnessId));
                        scratch.SetFloat(MetallicId, surface.Original.GetFloat(MetallicId));
                        SurfaceDamageLibrary.Apply(surface.Renderer, scratch, surface.Damage, room, 0f, surface.Material);
                    }
                    surface.Renderer.SetPropertyBlock(scratch);
                }
            IsSealed = false;
            IsSealPending = false;
            Physics.SyncTransforms();
        }

        private void OnDisable() => RestoreOrdinary();
        private void OnEnable() { if (IsInitialized) RefreshAppearance(desiredDay); }
    }
}
