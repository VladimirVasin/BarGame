using UnityEngine;

namespace BarPromenade
{
    /// <summary>Two street encounters share one board and one animation clock.</summary>
    internal sealed class CityPlaygroundSeesaw : MonoBehaviour
    {
        internal const float MountSeconds = 1.5f;
        private const float RockSeconds = 8f;
        internal const float DismountStart = MountSeconds + RockSeconds;
        internal const float Finish = DismountStart + MountSeconds;
        private const float RockDegrees = 7f;

        private readonly int[] riders = new int[2];
        private readonly bool[] ready = new bool[2];
        private Transform board;
        private Transform[] docks;
        private Vector3[] restDocks;
        private bool dismounting;

        internal CityCourtyardPlaygroundSite Site { get; private set; }
        internal float Seconds { get; private set; } = -1f;
        internal bool Playing => Seconds >= 0f;
        internal Transform Board => board;
        internal Vector3 Approach => transform.TransformPoint(CityCourtyardPlaygroundSite.SeesawApproach);

        internal void Initialize(CityCourtyardPlaygroundSite site, Transform movingBoard, Transform first, Transform second)
        {
            Site = site;
            board = movingBoard;
            docks = new[] { first, second };
            restDocks = new[] { first.position, second.position };
        }

        internal Vector3 Dock(int end) => restDocks[end];
        internal Vector3 Wing(int end) => Dock(end) + transform.TransformDirection(CityCourtyardPlaygroundSite.SeesawApproach);
        internal Vector3 MovingDock(int end) => docks[end].position;
        internal Quaternion Facing(int end, bool moving = false) =>
            (moving ? board.rotation : transform.rotation) * (end == 0 ? Quaternion.identity : HalfTurn);
        private static readonly Quaternion HalfTurn = Quaternion.Euler(0f, 180f, 0f);

        internal int FreeEnd => !isActiveAndEnabled || Playing ? -1 : riders[0] == 0 ? 0 : riders[1] == 0 ? 1 : -1;
        internal bool Owns(int end, int encounter) => isActiveAndEnabled && riders[end] == encounter;

        internal void Reserve(int end, int encounter) => riders[end] = encounter;

        internal void MarkReady(int end, bool waiting) => ready[end] = waiting;

        internal void Release(int end, int encounter)
        {
            if (riders[end] != encounter) return;
            riders[end] = 0;
            ready[end] = false;
            if (riders[0] == 0 && riders[1] == 0) ResetCycle();
        }

        internal void Advance(float delta, bool daylight)
        {
            if (!isActiveAndEnabled) return;
            if (!Playing)
            {
                if (daylight && ready[0] && ready[1]) Seconds = 0f;
                return;
            }

            if (Seconds >= Finish) return;
            float next = Mathf.Min(Finish, Seconds + delta);
            if (!dismounting && next >= DismountStart)
            {
                dismounting = (riders[0] == 0 || CityPlaygroundVisit.ClearPoint(restDocks[0])) &&
                    (riders[1] == 0 || CityPlaygroundVisit.ClearPoint(restDocks[1]));
                if (!dismounting) next = DismountStart;
            }
            Seconds = next;
            float rock = Mathf.Clamp01((Seconds - MountSeconds) / RockSeconds);
            float ease = Mathf.SmoothStep(0f, 1f, Mathf.Min(rock, 1f - rock) * 6f);
            float pitch = RockDegrees * Mathf.Sin(rock * 8f * Mathf.PI) * ease;
            board.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private void ResetCycle()
        {
            Seconds = -1f;
            dismounting = false;
            if (board != null) board.localRotation = Quaternion.identity;
        }

        private void OnDisable()
        {
            riders[0] = riders[1] = 0;
            ready[0] = ready[1] = false;
            ResetCycle();
        }
    }
}
