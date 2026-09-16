using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// A pooled street walker built from the default NPC population: one
    /// permanent `city.pedestrian.NN` identity dressed by the whole-world
    /// allocation, wearing a <see cref="CityPedestrianAssetRegistry"/> so the
    /// director, the routes, Route 01 and the personal-space rules drive it
    /// exactly like a library walker.
    ///
    /// It sits on the pooled presentation root ABOVE the character's own
    /// root. The actor resets a bound presentation's scale to one, and the
    /// character root carries the model's authored height scale; a wrapper
    /// keeps both true.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityPedestrianDefaultNpcBody : MonoBehaviour
    {
        public const string SourceGeneratorVersion = "DefaultNpcCatalog";

        [SerializeField] private string characterId;
        [SerializeField] private VillageResidentPresentation character;

        public string CharacterId => characterId;
        public VillageResidentPresentation Character => character;

        /// <summary>
        /// Builds the body for one registered walker and returns the registry
        /// on its presentation root. The registry's design ID is the catalog
        /// model the population chose, so speed and the seated ride resolve
        /// through the street archetype of that model.
        /// </summary>
        public static CityPedestrianAssetRegistry Create(
            Transform parent,
            string characterId,
            CityPedestrianAssetRegistry clipDonor)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            DefaultNpcPopulation.Assignment assignment =
                DefaultNpcPopulation.GetAssignment(characterId);
            GameObject root = new GameObject(
                $"Pedestrian {characterId} ({assignment.ModelId})");
            root.transform.SetParent(parent, false);
            VillageResidentPresentation actor = null;
            try
            {
                actor = DefaultNpcFactory.CreateForCharacter(
                    root.transform,
                    characterId);
                actor.name = "Character";
                // Face the wrapper's +Z the way every route-driven walker
                // does, measured live as the port and the fair measure it.
                CityPortCrew.AlignWorkerModelWithPlacement(actor);
                // From here on one graph writes this body: the pedestrian
                // presentation's. The village sampler rebuilds itself only
                // if someone calls it, and nobody on the street does.
                actor.ReleaseAnimation();
                // Steps are heard from the presentation root the registry
                // registers as it comes alive, not from two roots at once.
                NpcFootstepSources.Unregister(actor.transform);

                Transform model = actor.ModelRoot;
                AnimationClip idle = actor.GetClip(VillageResidentAction.Idle);
                AnimationClip walk = actor.GetClip(VillageResidentAction.Walk);
                if (model == null || actor.Animator == null ||
                    actor.Head == null || idle == null || walk == null)
                {
                    throw new InvalidOperationException(
                        $"Default NPC model '{assignment.ModelId}' has no " +
                        "complete rig or idle/walk pair for the street.");
                }

                Renderer[] visible = CollectVisibleRenderers(actor);
                var registry = root.AddComponent<CityPedestrianAssetRegistry>();
                registry.Configure(
                    actor.Animator,
                    model,
                    visible,
                    // No palette bindings: the wardrobe owns every colour
                    // and the hash-picked palette variant must not repaint
                    // a coat the population chose.
                    Array.Empty<CityPedestrianRendererBinding>(),
                    actor.Head,
                    RequireJoint(model, assignment.ModelId, "foot.L"),
                    RequireJoint(model, assignment.ModelId, "foot.R"),
                    idle,
                    walk,
                    CalculateLocalBounds(root.transform, visible),
                    CountTriangles(visible),
                    SourceGeneratorVersion,
                    assignment.ModelId,
                    assignment.VisibleSignature,
                    configuredPelvisAnchor:
                        RequireJoint(model, assignment.ModelId, "pelvis"),
                    configuredSitClip: clipDonor != null
                        ? clipDonor.SitClip
                        : null);
                if (clipDonor != null)
                {
                    registry.ConfigurePersonalSpaceClips(
                        clipDonor.PersonalSpaceGuardClip,
                        clipDonor.PersonalSpaceShoveClip);
                }

                var body = root.AddComponent<CityPedestrianDefaultNpcBody>();
                body.characterId = characterId;
                body.character = actor;
                return registry;
            }
            catch
            {
                if (actor != null)
                {
                    NpcFootstepSources.Unregister(actor.transform);
                }

                CityPedestrianResources.DestroyObject(root);
                throw;
            }
        }

        private static Transform RequireJoint(
            Transform model,
            string modelId,
            string name)
        {
            return CityPedestrianHandProps.FindSocket(model, name)
                ?? throw new InvalidOperationException(
                    $"Default NPC model '{modelId}' has no '{name}' joint.");
        }

        /// <summary>
        /// The renderers the population left showing. The wardrobe disables
        /// every garment it did not equip and every part a garment covers,
        /// and the assignment is permanent, so the visible set is fixed for
        /// the life of the body.
        /// </summary>
        private static Renderer[] CollectVisibleRenderers(
            VillageResidentPresentation actor)
        {
            Renderer[] all = actor.GetComponentsInChildren<Renderer>(true);
            var visible = new List<Renderer>(all.Length);
            for (int index = 0; index < all.Length; index++)
            {
                if (all[index].enabled)
                {
                    visible.Add(all[index]);
                }
            }

            return visible.ToArray();
        }

        private static Bounds CalculateLocalBounds(
            Transform root,
            IReadOnlyList<Renderer> renderers)
        {
            Bounds bounds = default;
            bool started = false;
            for (int index = 0; index < renderers.Count; index++)
            {
                Renderer renderer = renderers[index];
                Mesh mesh = SharedMesh(renderer);
                if (mesh == null)
                {
                    continue;
                }

                Bounds local = mesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new Vector3(
                        (corner & 1) == 0 ? local.min.x : local.max.x,
                        (corner & 2) == 0 ? local.min.y : local.max.y,
                        (corner & 4) == 0 ? local.min.z : local.max.z);
                    Vector3 inRoot = root.InverseTransformPoint(
                        renderer.transform.TransformPoint(point));
                    if (!started)
                    {
                        bounds = new Bounds(inRoot, Vector3.zero);
                        started = true;
                    }
                    else
                    {
                        bounds.Encapsulate(inRoot);
                    }
                }
            }

            return bounds;
        }

        private static int CountTriangles(IReadOnlyList<Renderer> renderers)
        {
            int triangles = 0;
            for (int index = 0; index < renderers.Count; index++)
            {
                Mesh mesh = SharedMesh(renderers[index]);
                if (mesh == null)
                {
                    continue;
                }

                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    triangles += (int)mesh.GetIndexCount(sub) / 3;
                }
            }

            return triangles;
        }

        private static Mesh SharedMesh(Renderer renderer)
        {
            return renderer is SkinnedMeshRenderer skinned
                ? skinned.sharedMesh
                : renderer.GetComponent<MeshFilter>()?.sharedMesh;
        }
    }
}
