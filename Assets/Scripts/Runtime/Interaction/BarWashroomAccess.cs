using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A physical visit reserves the room; only a closed occupied room is locked.</summary>
    [DisallowMultipleComponent]
    public sealed class BarWashroomAccess : MonoBehaviour
    {
        public const string OccupiedKey = "interaction.bar_wc_occupied";
        private BarWashroom washroom;
        private Transform room, hero, npcActor;
        private UnityEngine.Object npcOwner;
        private float npcRadius;
        private bool locked, npcExiting;

        public bool IsNpcReserved { get { PruneDestroyedOwner(); return npcOwner != null; } }
        public bool IsLocked { get { PruneDestroyedOwner(); return locked; } }
        public UnityEngine.Object NpcOwner { get { PruneDestroyedOwner(); return npcOwner; } }
        public Transform NpcActor { get { PruneDestroyedOwner(); return npcActor; } }
        public bool IsHeroInside => hero != null && room != null &&
            BarWashroomLayout.Contains(BarWashroomLayout.Bounds, room.InverseTransformPoint(hero.position));
        public bool IsHeroBlocking
        {
            get
            {
                if (washroom == null || room == null || hero == null) return true;
                Vector3 local = room.InverseTransformPoint(hero.position);
                bool doorway = local.x >= -4.98f && local.x <= -3.42f && local.z >= 3.52f && local.z <= 4.50f;
                return IsHeroInside || doorway || washroom.Door.OwnsActiveInteraction ||
                    washroom.Choice.IsOpen || washroom.Toilet.OwnsBathroomInteraction ||
                    washroom.Plunge.OwnsBathroomInteraction;
            }
        }

        public void Initialize(BarWashroom host, Transform authoredRoom, Transform player)
        {
            if (host == null || host.Door == null || authoredRoom == null || player == null)
                throw new ArgumentException("Washroom access requires the real room, door and hero.");
            washroom = host; room = authoredRoom; hero = player;
            washroom.Door.ConfigureWashroomAccess(this);
            washroom.Host.Availability = () => !IsLocked;
        }

        public bool TryReserveNpc(UnityEngine.Object owner, Transform actor, float radius = .18f, float height = 1.75f)
        {
            if (owner == null || actor == null || !isActiveAndEnabled || GameTimeScaleRuntime.IsPaused ||
                SceneTransitionService.IsTransitioning || !float.IsFinite(radius) || !float.IsFinite(height) ||
                radius <= 0f || height < radius * 2f || IsHeroBlocking) return false;
            PruneDestroyedOwner();
            if (npcOwner != null) return ReferenceEquals(npcOwner, owner) && npcActor == actor;
            npcOwner = owner; npcActor = actor; npcRadius = radius; locked = false; npcExiting = false;
            washroom.Door.RegisterNpcBody(owner, actor, radius, height);
            return true;
        }

        public bool TryLockNpc(UnityEngine.Object owner)
        {
            if (!Owns(owner) || GameTimeScaleRuntime.IsPaused || IsHeroBlocking || !washroom.Door.IsClosed)
                return false;
            Vector3 local = room.InverseTransformPoint(npcActor.position);
            if (!BarWashroomLayout.Contains(BarWashroomLayout.Bounds, local, npcRadius + .01f)) return false;
            locked = true;
            npcExiting = false;
            return true;
        }

        public bool BeginNpcExit(UnityEngine.Object owner)
        {
            if (!Owns(owner) || GameTimeScaleRuntime.IsPaused) return false;
            locked = false;
            Vector3 local = room.InverseTransformPoint(npcActor.position);
            // A body already crossing the threshold may yield outward. A
            // queued visitor in the hall cannot use exit priority to enter.
            npcExiting = BarWashroomLayout.Contains(BarWashroomLayout.Bounds, local) ||
                local.z >= BarWashroomLayout.DoorCenter.z - npcRadius &&
                Mathf.Abs(local.x - BarWashroomLayout.DoorCenter.x) <= BarWashroomLayout.DoorWidth * .5f + npcRadius;
            return true;
        }

        public bool ReleaseNpc(UnityEngine.Object owner)
        {
            if (!Owns(owner)) return false;
            // A disabled or paused owner still owns its occupied room. Only a
            // real exit or destruction frees it; there is no arbitrary timeout.
            Vector3 local = room.InverseTransformPoint(npcActor.position);
            if (BarWashroomLayout.Contains(BarWashroomLayout.Bounds, local)) return false;
            ClearOwner();
            return true;
        }

        public bool CanHeroOperateDoor(bool inside) => inside || !IsLocked;

        public bool CanNpcOperateDoor(UnityEngine.Object owner) => Owns(owner) &&
            !washroom.Door.OwnsActiveInteraction && (!IsHeroBlocking || locked || npcExiting);

        private bool Owns(UnityEngine.Object owner)
        {
            PruneDestroyedOwner();
            return owner != null && ReferenceEquals(owner, npcOwner) && npcActor != null;
        }

        private void PruneDestroyedOwner()
        {
            if (!ReferenceEquals(npcOwner, null) && (npcOwner == null || npcActor == null)) ClearOwner();
        }

        private void ClearOwner()
        {
            if (washroom != null && washroom.Door != null) washroom.Door.UnregisterNpcBody(npcOwner);
            npcOwner = null; npcActor = null; locked = false; npcExiting = false;
        }

        private void OnDestroy()
        {
            ClearOwner();
            if (washroom != null && washroom.Door != null) washroom.Door.ConfigureWashroomAccess(null);
            if (washroom != null && washroom.Host != null) washroom.Host.Availability = null;
        }
    }
}
