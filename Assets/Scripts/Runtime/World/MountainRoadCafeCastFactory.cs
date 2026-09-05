using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Instantiates the complete four-person staged composition. Missing
    /// production assets leave four semantic placement marks and one clear
    /// diagnostic; they never fall back to ambient walkers or primitives.
    /// </summary>
    public static class MountainRoadCafeCastFactory
    {
        public const string RuntimeRootName = "Authored Cafe Cast";

        public static MountainRoadCafeCastController Create(
            Transform parent,
            MountainRoadCafeCastPlan plan,
            IDictionary<string, Transform> semanticAnchors,
            int seed)
        {
            return Create(
                parent,
                plan,
                semanticAnchors,
                seed,
                MountainRoadCafeCastProvider.Load());
        }

        public static MountainRoadCafeCastController Create(
            Transform parent,
            MountainRoadCafeCastPlan plan,
            IDictionary<string, Transform> semanticAnchors,
            int seed,
            MountainRoadCafeCastProvider provider)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (semanticAnchors == null)
            {
                throw new ArgumentNullException(nameof(semanticAnchors));
            }

            if (plan.Members.Count !=
                MountainRoadCafeWorldBuilder.TableauNpcCount)
            {
                throw new ArgumentException(
                    "The mountain cafe cast plan requires four members.",
                    nameof(plan));
            }

            var castRoot = new GameObject(RuntimeRootName);
            castRoot.transform.SetParent(parent, false);
            var marks = new List<Transform>(plan.Members.Count);
            for (int index = 0; index < plan.Members.Count; index++)
            {
                MountainRoadCafeCastMemberPlan member =
                    plan.Members[index];
                var mark = new GameObject(member.Name + " Mark");
                mark.transform.SetParent(castRoot.transform, false);
                mark.transform.SetPositionAndRotation(
                    member.Position,
                    Quaternion.LookRotation(
                        member.Facing,
                        Vector3.up));
                marks.Add(mark.transform);
                semanticAnchors.Add(member.StableId, mark.transform);
            }

            if (provider == null || !provider.HasCompleteCast)
            {
                GameLog.Error(
                    "mountain_road",
                    "cafe_cast_provider_missing");
                return null;
            }

            try
            {
                ValidateProvider(provider, plan);
                var presentations = new List<
                    MountainRoadCafeCastPresentation>(plan.Members.Count);
                for (int index = 0; index < plan.Members.Count; index++)
                {
                    MountainRoadCafeCastMemberPlan member =
                        plan.Members[index];
                    GameObject instance = UnityEngine.Object.Instantiate(
                        provider.GetPrefab(member.Role),
                        marks[index],
                        false);
                    instance.name = member.Name;
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;

                    var registry = instance.GetComponentInChildren<
                        MountainRoadCafeCastAssetRegistry>(true);
                    registry.ApplyBaseColors();
                    registry.Animator.applyRootMotion = false;
                    // The menu close-up can keep the attendant off-screen
                    // while a world prop still follows the animated hand.
                    registry.Animator.cullingMode =
                        member.Role == MountainRoadCafeCastRole.Attendant
                            ? AnimatorCullingMode.AlwaysAnimate
                            : AnimatorCullingMode.CullUpdateTransforms;

                    var presentation = instance.AddComponent<
                        MountainRoadCafeCastPresentation>();
                    // Hand props BEFORE Initialize and before anything
                    // reads the attendant's pour spout: the spout anchor
                    // lives on the coffee-pot prop, and the presentation's
                    // clip changes route the pot's visibility through the
                    // registry, which remembers the request until the pot
                    // is there.
                    AttachHandProps(registry, presentation, member.Role);
                    presentation.Initialize(
                        registry,
                        member.Role,
                        member.IdlePhaseSeconds);
                    if (member.Role ==
                        MountainRoadCafeCastRole.PairWoman)
                    {
                        var cigarette = instance.AddComponent<
                            MountainRoadCafeCigaretteEffect>();
                        cigarette.Initialize(presentation, registry);
                    }

                    presentations.Add(presentation);
                }

                var controller = castRoot.AddComponent<
                    MountainRoadCafeCastController>();
                controller.Initialize(presentations, seed);
                return controller;
            }
            catch
            {
                DestroyObject(castRoot);
                throw;
            }
        }

        /// <summary>
        /// What each role holds: the woman her cafe cigarette, the
        /// attendant the towel in his left hand and the pot in his right.
        /// The cafe bodies are not <see cref="CityPedestrianAssetRegistry"/>
        /// bodies, so the socket is found by name off the model root and
        /// the material copied from the body's first bound renderer; the
        /// palette is the props' authored one (variant 0), because the
        /// cafe cast wears manifest colours, not the street's variants.
        /// </summary>
        private static void AttachHandProps(
            MountainRoadCafeCastAssetRegistry registry,
            MountainRoadCafeCastPresentation presentation,
            MountainRoadCafeCastRole role)
        {
            switch (role)
            {
                case MountainRoadCafeCastRole.PairWoman:
                    presentation.RegisterHandProp(
                        AttachHandProp(
                            registry,
                            CityPedestrianHandPropId.CafeCigarette));
                    break;
                case MountainRoadCafeCastRole.Attendant:
                    presentation.RegisterHandProp(
                        AttachHandProp(
                            registry,
                            CityPedestrianHandPropId.ServiceTowel));
                    CityPedestrianHandPropRegistry pot = AttachHandProp(
                        registry,
                        CityPedestrianHandPropId.CoffeePot);
                    presentation.RegisterHandProp(pot);
                    registry.AttachCoffeePot(pot);
                    break;
            }
        }

        private static CityPedestrianHandPropRegistry AttachHandProp(
            MountainRoadCafeCastAssetRegistry registry,
            CityPedestrianHandPropId id)
        {
            string socketName = CityPedestrianHandProps.GetSocketName(id);
            Transform socket = registry.FindModelTransform(socketName);
            if (socket == null)
            {
                throw new InvalidOperationException(
                    "The cafe " + registry.Role + " prefab has no '" +
                    socketName + "' socket for the " + id + " hand prop.");
            }

            Material material = null;
            for (int index = 0;
                 index < registry.RendererBindings.Count && material == null;
                 index++)
            {
                Renderer renderer = registry.RendererBindings[index]?.Renderer;
                if (renderer != null)
                {
                    material = renderer.sharedMaterial;
                }
            }

            return CityPedestrianHandProps.Attach(socket, id, material, 0);
        }

        private static void ValidateProvider(
            MountainRoadCafeCastProvider provider,
            MountainRoadCafeCastPlan plan)
        {
            var uniquePrefabs = new HashSet<GameObject>();
            for (int index = 0; index < plan.Members.Count; index++)
            {
                MountainRoadCafeCastMemberPlan member =
                    plan.Members[index];
                GameObject prefab = provider.GetPrefab(member.Role);
                if (prefab == null || !uniquePrefabs.Add(prefab))
                {
                    throw new InvalidOperationException(
                        "Each cafe cast role requires a unique staged prefab.");
                }

                ValidatePrefab(prefab, member.Role);
            }
        }

        private static void ValidatePrefab(
            GameObject prefab,
            MountainRoadCafeCastRole role)
        {
            MountainRoadCafeCastAssetRegistry[] registries =
                prefab.GetComponentsInChildren<
                    MountainRoadCafeCastAssetRegistry>(true);
            if (registries.Length != 1)
            {
                throw new InvalidOperationException(
                    "The " + role + " prefab requires exactly one " +
                    nameof(MountainRoadCafeCastAssetRegistry) + ".");
            }

            MountainRoadCafeCastAssetRegistry registry = registries[0];
            if (registry.Animator == null ||
                registry.ModelRoot == null ||
                registry.Role != role ||
                registry.IdleClip == null ||
                registry.BeatClip == null)
            {
                throw new InvalidOperationException(
                    "The " + role +
                    " prefab has an incomplete cafe cast registry.");
            }

            int expectedClipCount = role switch
            {
                MountainRoadCafeCastRole.LonePatron => 2,
                MountainRoadCafeCastRole.Attendant => 4,
                _ => 2
            };
            if (registry.ClipBindings.Count != expectedClipCount)
            {
                throw new InvalidOperationException(
                    "The " + role + " prefab has the wrong authored " +
                    "clip count.");
            }

            // The spout anchor lives on the coffee-pot hand prop now. A
            // body still carrying one is a stale build whose baked pot
            // would double up with the attached prop.
            if (registry.FindModelTransform(
                    CityPedestrianHandProps.CoffeePotSpoutAnchorName) != null)
            {
                throw new InvalidOperationException(
                    "The cafe " + role + " prefab still carries '" +
                    CityPedestrianHandProps.CoffeePotSpoutAnchorName +
                    "'; the spout anchor lives on the coffee-pot hand prop.");
            }

            if (registry.RendererBindings.Count == 0)
            {
                throw new InvalidOperationException(
                    "The " + role +
                    " prefab requires authored renderer colour bindings.");
            }

            for (int index = 0;
                 index < registry.RendererBindings.Count;
                 index++)
            {
                MountainRoadCafeCastRendererBinding binding =
                    registry.RendererBindings[index];
                if (binding == null || binding.Renderer == null)
                {
                    throw new InvalidOperationException(
                        "The " + role +
                        " prefab has an unbound cafe renderer colour.");
                }
            }

            if (prefab.GetComponentInChildren<Collider>(true) != null ||
                prefab.GetComponentInChildren<Collider2D>(true) != null ||
                prefab.GetComponentInChildren<Rigidbody>(true) != null ||
                prefab.GetComponentInChildren<Rigidbody2D>(true) != null ||
                prefab.GetComponentInChildren<AudioSource>(true) != null ||
                prefab.GetComponentInChildren<Light>(true) != null ||
                prefab.GetComponentInChildren<Camera>(true) != null)
            {
                throw new InvalidOperationException(
                    "The " + role +
                    " staged cafe prefab must remain passive.");
            }
        }

        private static void DestroyObject(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
