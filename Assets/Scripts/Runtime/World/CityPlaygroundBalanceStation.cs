using UnityEngine;

namespace BarPromenade
{
    internal sealed class CityPlaygroundBalancePlan
    {
        internal CityPlaygroundBalancePlan(Vector3 position, Quaternion rotation, Vector3 join,
            Vector3 branch, Rect footprint, bool block)
        {
            Position = position;
            Rotation = rotation;
            RouteJoin = join;
            Branch = branch;
            Footprint = footprint;
            Block = block;
        }

        internal Vector3 Position { get; }
        internal Quaternion Rotation { get; }
        internal Vector3 RouteJoin { get; }
        internal Vector3 Branch { get; }
        internal Rect Footprint { get; }
        internal bool Block { get; }
    }

    internal sealed class CityPlaygroundBalanceStation : MonoBehaviour
    {
        internal CityPlaygroundBalancePlan Plan { get; private set; }
        internal Transform Dock { get; private set; }
        internal Transform Top { get; private set; }
        internal int Visitor { get; private set; }
        internal bool Available => isActiveAndEnabled && Visitor == 0;

        internal void Initialize(CityPlaygroundBalancePlan plan, Transform dock, Transform top)
        {
            Plan = plan;
            Dock = dock;
            Top = top;
        }

        internal void Reserve(int encounter) => Visitor = encounter;
        internal void Release(int encounter)
        {
            if (Visitor == encounter) Visitor = 0;
        }
        private void OnDisable() => Visitor = 0;
    }
}
