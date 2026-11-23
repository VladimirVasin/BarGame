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
                var presentations = new List<
                    MountainRoadCafeCastPresentation>(plan.Members.Count);
                for (int index = 0; index < plan.Members.Count; index++)
                {
                    MountainRoadCafeCastMemberPlan member =
                        plan.Members[index];
                    var registry = CreateBody(marks[index], member.Role, provider);
                    GameObject instance = registry.gameObject;
                    instance.name = member.Name;
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
                    if (member.Role == MountainRoadCafeCastRole.Attendant)
                    {
                        NpcNameplateTarget.Attach(
                            instance,
                            "npc.name.cafe_attendant",
                            registry.FindModelTransform("head"),
                            "cafe_attendant").SetHeadClearance(0.4f);
                    }

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

        public static MountainRoadCafeCastAssetRegistry CreateBody(Transform parent, MountainRoadCafeCastRole role,
            MountainRoadCafeCastProvider provider)
        {
            var actor = DefaultNpcFactory.CreateForCharacter(parent, DefaultNpcPopulation.CafeCharacterId(role),
                initializeLocomotion: false);
            // The seated, pouring and handoff clips use the metre rig and fixed world docks.
            actor.transform.localScale = Vector3.one;
            var registry = actor.gameObject.AddComponent<MountainRoadCafeCastAssetRegistry>();
            registry.Configure(actor, provider.ForRole(role));
            return registry;
        }

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

            return CityPedestrianHandProps.Attach(socket, id, registry.BodyRenderers[0].sharedMaterial, 0);
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
