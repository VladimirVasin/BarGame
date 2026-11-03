using System;

namespace BarPromenade
{
    /// <summary>A bounded shelf source participates in its leaf's availability and presentation lifetime.</summary>
    internal interface IWarehouseLockerStock
    {
        bool IsPresenting { get; }
        void SetSourceAvailability(Func<bool> available, object priorityGroup);
        void CancelPickup();
    }
}
