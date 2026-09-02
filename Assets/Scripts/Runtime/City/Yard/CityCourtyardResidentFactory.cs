using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Instantiates the small fixed courtyard cast from generic, unlimited
    /// pedestrian archetypes. These residents never join the route director:
    /// they own no actor capsule, interaction, speech, sound or light.
    /// </summary>
    public static class CityCourtyardResidentFactory
    {
        public const string RuntimeRootName = "Courtyard Residents";

        public static IReadOnlyList<CityCourtyardResidentPresentation> Create(
            Transform parent,
            CityCourtyardResidentPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!plan.IsPresent)
            {
                return Array.Empty<CityCourtyardResidentPresentation>();
            }

            Transform root = new GameObject(RuntimeRootName).transform;
            root.SetParent(parent, false);
            var presentations =
                new List<CityCourtyardResidentPresentation>(plan.Count);
            var prefabs = new Dictionary<string, GameObject>(
                StringComparer.Ordinal);
            try
            {
                for (int index = 0; index < plan.Residents.Count; index++)
                {
                    CityCourtyardResidentDescriptor descriptor =
                        plan.Residents[index];
                    CityPedestrianArchetype archetype =
                        ResolveArchetype(descriptor.DesignId);
                    if (!prefabs.TryGetValue(
                            archetype.DesignId,
                            out GameObject prefab))
                    {
                        prefab = CityPedestrianResources.LoadPrefab(archetype);
                        if (prefab == null)
                        {
                            throw new InvalidOperationException(
                                $"Generic pedestrian prefab for " +
                                $"'{archetype.DesignId}' is missing at " +
                                $"Resources/{archetype.PrefabResourcePath}.");
                        }

                        prefabs.Add(archetype.DesignId, prefab);
                    }

                    if (!CityPedestrianResources.TryInstantiate(
                            prefab,
                            root,
                            out CityPedestrianAssetRegistry registry))
                    {
                        throw new InvalidOperationException(
                            $"Generic pedestrian prefab for " +
                            $"'{archetype.DesignId}' has no " +
                            nameof(CityPedestrianAssetRegistry) +
                            " on its root.");
                    }

                    registry.gameObject.name =
                        $"Courtyard Resident {index + 1:00} " +
                        $"({descriptor.Activity})";
                    ValidatePassivePresentation(registry.gameObject);
                    ApplyCourtyardProps(registry);
                    var presentation = registry.gameObject.AddComponent<
                        CityCourtyardResidentPresentation>();
                    presentation.Initialize(
                        registry,
                        archetype,
                        descriptor);
                    presentations.Add(presentation);
                }

                AttachNardiExchange(presentations);

                GameLog.Info(
                    "city",
                    "courtyard_residents_spawned",
                    GameLog.Field("count", presentations.Count));
                return presentations;
            }
            catch
            {
                CityPedestrianResources.DestroyObject(root.gameObject);
                throw;
            }
        }

        /// <summary>
        /// The one prop a courtyard body must put down.
        ///
        /// The babushka's prefab ships both of her hand props enabled so the
        /// drying yard can pick one per role, and the street strips both. Here
        /// she keeps the CIGARETTE - it is half of what `BabushkaSmoke` is
        /// about - and loses the carpet beater, which belongs to a hung carpet
        /// and would read as a woman about to hit a bicycle with it.
        /// </summary>
        private static readonly string[] BabushkaCourtyardHidden =
        {
            "ACC_BeaterHandle",
            "ACC_BeaterNeck",
            "ACC_BeaterPaddleRise",
            "ACC_BeaterPaddleTip"
        };

        private static void ApplyCourtyardProps(
            CityPedestrianAssetRegistry registry)
        {
            if (registry != null &&
                string.Equals(
                    registry.DesignId,
                    CityPedestrianResources.BabushkaDesignId,
                    StringComparison.Ordinal))
            {
                CityPedestrianHeldProps.Hide(
                    registry,
                    BabushkaCourtyardHidden);
            }
        }

        /// <summary>
        /// The two men over a backgammon board, paired up and given the one
        /// motion a seated body here can have.
        ///
        /// Done AFTER the whole loop rather than inside it, because each of
        /// the pair needs the other's head and the second one does not exist
        /// yet while the first is being built. Paired by
        /// `SourceStableId` - the pocket they share - so a city with two
        /// nardi pockets never crosses them.
        ///
        /// Silent on every shape it does not recognise: a pocket with one
        /// seated player, a rig without a neck, a design whose bones are
        /// named differently. The bodies still sit there playing their clip;
        /// they simply do not look up.
        /// </summary>
        private static void AttachNardiExchange(
            IReadOnlyList<CityCourtyardResidentPresentation> residents)
        {
            for (int first = 0; first < residents.Count; first++)
            {
                CityCourtyardResidentPresentation a = residents[first];
                if (a == null ||
                    a.Descriptor.Activity !=
                        CityCourtyardResidentActivity.NardiPlayer)
                {
                    continue;
                }

                for (int second = first + 1;
                     second < residents.Count;
                     second++)
                {
                    CityCourtyardResidentPresentation b = residents[second];
                    if (b == null ||
                        b.Descriptor.Activity !=
                            CityCourtyardResidentActivity.NardiPlayer ||
                        !string.Equals(
                            a.Descriptor.SourceStableId,
                            b.Descriptor.SourceStableId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // The board is where each of them is already facing: the
                    // descriptor's facing is the vector from his own dock to
                    // the shared target, so a point along it is the board
                    // without the plan having to carry it twice.
                    Vector3 boardPoint =
                        (a.Descriptor.Position +
                         a.Descriptor.Facing * BoardReach +
                         b.Descriptor.Position +
                         b.Descriptor.Facing * BoardReach) * 0.5f;
                    int seed = a.Descriptor.SourceStableId != null
                        ? a.Descriptor.SourceStableId.GetHashCode()
                        : 0;
                    Bind(a, b, boardPoint, seed, false);
                    Bind(b, a, boardPoint, seed, true);
                    break;
                }
            }
        }

        /// <summary>How far in front of each man the board sits, along the
        /// facing his dock was solved with.</summary>
        private const float BoardReach = 0.62f;

        private static void Bind(
            CityCourtyardResidentPresentation self,
            CityCourtyardResidentPresentation partner,
            Vector3 boardPoint,
            int seed,
            bool secondSeat)
        {
            Transform neck = FindBone(self, "neck");
            Transform head = FindBone(self, "head");
            Transform partnerHead = FindBone(partner, "head");
            if (neck == null || head == null)
            {
                return;
            }

            var board = new GameObject("Nardi Board Point").transform;
            board.SetParent(self.transform, false);
            board.position = boardPoint;
            self.gameObject
                .AddComponent<CityCourtyardResidentLook>()
                .Initialize(
                    neck,
                    head,
                    partnerHead,
                    board,
                    seed,
                    secondSeat);
        }

        /// <summary>
        /// One bone by name off the drawn body. A name scan rather than a
        /// serialized field on purpose: adding a field to
        /// `CityPedestrianAssetRegistry` would dirty fourteen prefabs and
        /// force an asset rebuild for two transforms nothing else wants.
        /// </summary>
        private static Transform FindBone(
            CityCourtyardResidentPresentation resident,
            string boneName)
        {
            if (resident == null)
            {
                return null;
            }

            Transform[] all =
                resident.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < all.Length; index++)
            {
                if (string.Equals(
                        all[index].name,
                        boneName,
                        StringComparison.Ordinal))
                {
                    return all[index];
                }
            }

            return null;
        }

        private static CityPedestrianArchetype ResolveArchetype(
            string designId)
        {
            if (!CityPedestrianResources.TryGetArchetype(
                    designId,
                    out CityPedestrianArchetype archetype) ||
                archetype.MaximumPoolInstances !=
                    CityPedestrianArchetype.UnlimitedPoolInstances ||
                archetype.CarriesBoilingKettle ||
                !CityCourtyardResidentPlan.IsAllowedDesignId(designId))
            {
                throw new InvalidOperationException(
                    $"Courtyard resident design '{designId}' is not an " +
                    "allowed unlimited, passive generic archetype.");
            }

            return archetype;
        }

        private static void ValidatePassivePresentation(GameObject instance)
        {
            if (instance.GetComponentInChildren<Collider>(true) != null ||
                instance.GetComponentInChildren<Collider2D>(true) != null ||
                instance.GetComponentInChildren<Rigidbody>(true) != null ||
                instance.GetComponentInChildren<Rigidbody2D>(true) != null ||
                instance.GetComponentInChildren<AudioSource>(true) != null ||
                instance.GetComponentInChildren<Light>(true) != null ||
                instance.GetComponentInChildren<Camera>(true) != null)
            {
                throw new InvalidOperationException(
                    "Courtyard resident presentations must stay " +
                    "colliderless, silent and unlit.");
            }

            MonoBehaviour[] behaviours =
                instance.GetComponentsInChildren<MonoBehaviour>(true);
            for (int index = 0; index < behaviours.Length; index++)
            {
                if (behaviours[index] is IInteractable)
                {
                    throw new InvalidOperationException(
                        "Courtyard resident presentations must not be " +
                        "interactive.");
                }
            }
        }
    }
}
