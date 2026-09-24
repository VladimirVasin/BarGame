using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One owner of exterior sound transmission through village rooms.
    /// Local stove, footsteps and household voices retain their own acoustics.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1100)]
    public sealed class VillageInteriorAcoustics : MonoBehaviour
    {
        public const float TransitionSeconds = .35f;
        public const float LodgeClosedVolumeMultiplier = .025f;
        public const float LodgeClosedCutoffMultiplier = .24f;

        private AlpineVillageRoot village;
        private LodgeShelterController lodge;
        private VillageLifeAudio lodgeContacts;
        private float lodgeProfileBlend;
        private float lodgeEnclosureStart;
        private float lodgeEnclosureTarget;
        private float lodgeEnclosureElapsed = TransitionSeconds;

        public float LodgeEnclosure { get; private set; }
        public float Enclosure { get; private set; }
        public bool IsInsideLodge => village != null && lodge != null &&
            village.Player.GameObject != null && lodge.ContainsInterior(village.Player.GameObject.transform.position);

        public void Initialize(AlpineVillageRoot owner, LodgeShelterController shelter)
        {
            if (owner == null || shelter == null || owner.Workroom == null)
                throw new ArgumentException("Village acoustics require the live lodge and workroom.");
            village = owner;
            lodge = shelter;
            if (lodgeContacts == null)
            {
                var contactObject = new GameObject("Lodge floor contacts");
                contactObject.transform.SetParent(transform, false);
                lodgeContacts = contactObject.AddComponent<VillageLifeAudio>();
                lodgeContacts.Initialize();
            }
            village.Workroom.Environment.SetInteriorAcoustics(this);
        }

        public static float EvaluateLodgeEnclosure(bool inside, int openDoorCount)
        {
            if (!inside) return 0f;
            return openDoorCount <= 0 ? 1f : openDoorCount == 1 ? .44f : .18f;
        }

        private void LateUpdate() => Advance(Time.deltaTime);

        public void Advance(float dt)
        {
            if (village == null || lodge == null || dt < 0f || GameTimeScaleRuntime.IsPaused) return;
            bool riding = GameSessionState.IsRidingAVehicle;
            bool inside = !riding && IsInsideLodge;
            float step = dt / TransitionSeconds;
            float target = EvaluateLodgeEnclosure(inside, lodge.OpenDoorCount);
            if (target != lodgeEnclosureTarget)
            {
                lodgeEnclosureStart = LodgeEnclosure;
                lodgeEnclosureTarget = target;
                lodgeEnclosureElapsed = 0f;
            }
            lodgeEnclosureElapsed = Mathf.Min(TransitionSeconds, lodgeEnclosureElapsed + dt);
            LodgeEnclosure = Mathf.Lerp(lodgeEnclosureStart, lodgeEnclosureTarget,
                Mathf.SmoothStep(0f, 1f, lodgeEnclosureElapsed / TransitionSeconds));
            lodgeProfileBlend = Mathf.MoveTowards(lodgeProfileBlend, inside ? 1f : 0f, step);
            VillageWorkroomEnvironment workroom = village.Workroom != null ? village.Workroom.Environment : null;
            float workroomEnclosure = !riding && workroom != null && workroom.isActiveAndEnabled
                ? workroom.Enclosure : 0f;
            Enclosure = Mathf.Max(LodgeEnclosure, workroomEnclosure);
            // Vehicles retain their existing wind owner. Room state continues
            // releasing in the background so disembarkation cannot keep an old room.
            if (!riding)
                village.WindSound?.SetEnclosure(Enclosure,
                    Mathf.Lerp(MountainRoadWindSoundPlayer.EnclosedVolumeMultiplier,
                        LodgeClosedVolumeMultiplier, lodgeProfileBlend),
                    Mathf.Lerp(MountainRoadWindSoundPlayer.EnclosedCutoffMultiplier,
                        LodgeClosedCutoffMultiplier, lodgeProfileBlend));
            village.Soundscape?.SetListenerEnclosure(riding ? 0f : Enclosure);
        }

        public bool TryPlayLodgeFootstep(Vector3 position, float runBlend)
        {
            if (!isActiveAndEnabled || lodge == null || lodgeContacts == null || !lodge.ContainsInterior(position))
                return false;
            lodgeContacts.PlayWood(position, Mathf.Lerp(.20f, .28f, Mathf.Clamp01(runBlend)));
            return true;
        }

        private void OnDisable()
        {
            LodgeEnclosure = Enclosure = lodgeProfileBlend = 0f;
            lodgeEnclosureStart = lodgeEnclosureTarget = 0f;
            lodgeEnclosureElapsed = TransitionSeconds;
            if (village == null) return;
            if (!GameSessionState.IsRidingAVehicle) village.WindSound?.SetEnclosure(0f);
            village.Soundscape?.SetListenerEnclosure(0f);
        }
    }
}
