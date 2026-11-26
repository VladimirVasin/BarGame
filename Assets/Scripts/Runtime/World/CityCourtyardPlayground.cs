using UnityEngine;

namespace BarPromenade
{
    /// <summary>A fitted playground route with one visitor at a time.</summary>
    internal sealed class CityCourtyardPlayground : MonoBehaviour
    {
        internal readonly struct Crossing
        {
            internal Crossing(Transform entry, Transform exit, CityFairChildAction action, Transform[] contacts,
                float descentTime = 0f, Vector3 forward = default)
            {
                Entry = entry;
                Exit = exit;
                Action = action;
                Contacts = contacts;
                DescentTime = descentTime;
                Facing = Quaternion.LookRotation(forward == Vector3.zero ? exit.position - entry.position : forward);
            }

            internal Transform Entry { get; }
            internal Transform Exit { get; }
            internal CityFairChildAction Action { get; }
            internal Transform[] Contacts { get; }
            internal float DescentTime { get; }
            internal Quaternion Facing { get; }
        }

        internal Crossing Bars { get; private set; }
        internal Crossing Beam { get; private set; }
        internal Crossing First => Bars;
        internal Crossing Alternate { get; private set; }
        internal Crossing Next => useAlternate ? Alternate : First;
        private bool useAlternate;
        internal CityCourtyardPlaygroundSite Site { get; private set; }
        internal int Visitor { get; private set; }
        internal bool CanReserve => isActiveAndEnabled && Visitor == 0;
        internal Vector3 FarTurn => transform.TransformPoint(new Vector3(0f, 0f, 1.66f));
        internal Vector3 NearTurn => transform.TransformPoint(new Vector3(0f, 0f, -1.66f));
        internal Vector3 SideExit => transform.TransformPoint(Site.SideEntry - 2f * Vector3.forward * Site.SideEntry.z);
        internal Vector3 SideEntry => transform.TransformPoint(Site.SideEntry);

        internal void Initialize(CityCourtyardPlaygroundSite site, Crossing first, Crossing beam = default,
            Crossing alternate = default)
        {
            Site = site;
            Bars = first;
            Beam = beam;
            Alternate = alternate;
        }

        internal bool TryReserve(int encounter)
        {
            if (!CanReserve || encounter <= 0) return false;
            Visitor = encounter;
            return true;
        }

        internal void Release(int encounter)
        {
            if (Visitor == encounter) Visitor = 0;
        }

        internal void CompleteCrossing(int encounter)
        {
            if (Visitor == encounter && Alternate.Entry != null) useAlternate = !useAlternate;
        }

        private void OnDisable() => Visitor = 0;
    }
}
