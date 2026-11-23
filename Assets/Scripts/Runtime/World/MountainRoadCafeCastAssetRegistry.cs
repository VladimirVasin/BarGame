using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum MountainRoadCafeCastClipKind
    {
        Idle = 0,
        Drink = 1,
        Wipe = 2,
        Walk = 3,
        Pour = 4,
        Notice = 5,
        Interject = 6,
        ClosingAddressEnter = 7,
        ClosingAddressLoop = 8,
        ClosingAddressExit = 9
    }

    [Serializable]
    public sealed class MountainRoadCafeCastClipBinding
    {
        [SerializeField] private MountainRoadCafeCastClipKind kind;
        [SerializeField] private AnimationClip clip;
        [SerializeField] private bool loop;

        public MountainRoadCafeCastClipBinding(
            MountainRoadCafeCastClipKind configuredKind,
            AnimationClip configuredClip,
            bool configuredLoop)
        {
            kind = configuredKind;
            clip = configuredClip ??
                throw new ArgumentNullException(nameof(configuredClip));
            loop = configuredLoop;
        }

        public MountainRoadCafeCastClipKind Kind => kind;
        public AnimationClip Clip => clip;
        public bool Loop => loop;
    }

    /// <summary>Binds the allocated resident to the cafe's clips, sockets and carried pot.</summary>
    public sealed class MountainRoadCafeCastAssetRegistry : MonoBehaviour
    {
        private CafeRoleAnimations animations;
        private readonly Dictionary<string, Transform> joints = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private CityPedestrianHandPropRegistry coffeePot;
        private bool coffeePotVisible;
        public Renderer[] BodyRenderers { get; private set; }
        public Animator Animator { get; private set; }
        public Transform ModelRoot { get; private set; }
        public MountainRoadCafeCastRole Role => animations.Role;
        public MountainRoadCafeCastClipKind DefaultClipKind => Role == MountainRoadCafeCastRole.Attendant
            ? MountainRoadCafeCastClipKind.Wipe : MountainRoadCafeCastClipKind.Idle;
        public IReadOnlyList<MountainRoadCafeCastClipBinding> ClipBindings => animations.Clips;
        public AnimationClip IdleClip => GetClip(DefaultClipKind);
        public AnimationClip BeatClip => GetClip(Role switch
        {
            MountainRoadCafeCastRole.Attendant => MountainRoadCafeCastClipKind.Notice,
            MountainRoadCafeCastRole.LonePatron => MountainRoadCafeCastClipKind.Interject,
            _ => MountainRoadCafeCastClipKind.Drink
        });
        public CityPedestrianHandPropRegistry CoffeePot => coffeePot;
        public bool IsCoffeePotVisible => coffeePotVisible;

        public void Configure(VillageResidentPresentation actor, CafeRoleAnimations roleAnimations)
        {
            animations = roleAnimations;
            Animator = actor.Animator;
            ModelRoot = actor.ModelRoot;
            BodyRenderers = ModelRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var joint in ModelRoot.GetComponentsInChildren<Transform>(true))
                joints.TryAdd(joint.name, joint);
            NpcSkinnedMeshCullingGuard.EnableDynamicBounds(ModelRoot);
        }

        public AnimationClip GetClip(MountainRoadCafeCastClipKind kind) =>
            animations.TryGetClip(kind, out var clip, out _) ? clip : null;
        public bool TryGetClip(MountainRoadCafeCastClipKind kind, out AnimationClip clip, out bool loop) =>
            animations.TryGetClip(kind, out clip, out loop);
        public Transform FindModelTransform(string jointName) =>
            joints.TryGetValue(jointName, out var joint) ? joint : null;

        public void AttachCoffeePot(CityPedestrianHandPropRegistry pot)
        {
            if (coffeePot != null && coffeePot != pot)
            {
                CityPedestrianHandProps.Detach(ref coffeePot);
            }

            coffeePot = pot;
            if (coffeePot != null)
            {
                coffeePot.SetVisible(coffeePotVisible);
            }
        }

        /// <summary>
        /// Null-safe: remembers the request even while no pot is
        /// attached, and routes it to the attached prop as a whole. The
        /// body carries no pot renderer of its own any more.
        /// </summary>
        public void SetCoffeePotVisible(bool visible)
        {
            coffeePotVisible = visible;
            if (coffeePot != null)
            {
                coffeePot.SetVisible(visible);
            }
        }

        private void OnDisable() => NpcFootstepSources.Unregister(transform);
        private void OnEnable() => NpcFootstepSources.Register(transform);
    }
}
